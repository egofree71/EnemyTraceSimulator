-- ladybug_preferred_pc_trace.lua
--
-- Targeted MAME debugger trace for Lady Bug EnemyWork.preferred[] writes.
--
-- v0.9.13c extends the previous preferred[] exact-PC diagnostic so the
-- random/R-low branch can be inspected at the real source PCs instead of trying
-- to infer it from frame-boundary R values.
--
-- IMPORTANT:
--   This is not a normal JSONL gameplay trace. It produces exact-PC diagnostic
--   output through MAME debugger logerror lines. Do not load the resulting files
--   with the normal "Load trace" workflow.
--
-- The C# launcher recognizes this exact filename and automatically enables:
--   -debug -log -debugscript ladybug_preferred_pc_debug_startup.cmd
--
-- Stable LBPREF write markers kept for the existing C# analyzer:
--   0x2E97 : base preferred generation, player-dir / rotate branch write
--   0x2EC7 : base preferred generation, random LD A,R branch write
--   0x477D : chase/BFS override writing preferred[]
--
-- Extra LBRND markers added for random/R-low investigation:
--   0x2E5C : base preferred[] generator entry
--   0x2E6A : threshold compare using 61B9
--   0x2E84 : input read result before zero/non-zero branch
--   0x2E8C : rotate branch selected, A contains PLAYER_DIR_CURRENT
--   0x2E9E : random branch selected
--   0x2EA3 : random loop before LD A,R
--   0x2EA5 : immediately after LD A,R; A contains the true R value used
--   0x2ECB : base generator calls BFS/chase override
--   0x46D8 : BFS/chase override entry
--
-- Output files:
--   <output_prefix>_preferred_pc_hits.log
--   <output_prefix>_preferred_pc_summary.txt
--   error.log  (primary raw MAME -log output in tools/mame/lua)

local CONFIG = {
    output_prefix = "ladybug_sequence_v8_pcdiag",
    output_dir = ".",

    save_state = "test1",
    load_after_frames = 5,
    frames_after_tick0_to_capture = 600,

    exit_when_done = true,
    pause_when_done = false,
    flush_every_trace_line = true,
}

local function merge_runtime_config()
    local cfg_path = "ladybug_sequence_runtime_config.lua"
    local f = io.open(cfg_path, "r")
    if not f then return end
    f:close()

    local ok, user_cfg = pcall(dofile, cfg_path)
    if not ok then
        emu.print_warning("Lady Bug preferred PC trace: could not load " .. cfg_path .. ": " .. tostring(user_cfg))
        return
    end

    if type(user_cfg) ~= "table" then
        emu.print_warning("Lady Bug preferred PC trace: " .. cfg_path .. " did not return a table")
        return
    end

    for k, v in pairs(user_cfg) do
        CONFIG[k] = v
    end

    emu.print_info("Lady Bug preferred PC trace: loaded runtime config from " .. cfg_path)
end

merge_runtime_config()

local cpu = nil
local mem = nil
local cpu_debug = nil
local machine_debugger = nil

local hits_file = nil
local summary_file = nil

local initialized = false
local load_requested = false
local load_call_ok = nil
local load_call_error = ""
local post_load_seen = false
local capture_started = false
local done = false

local mame_frame = 0
local frames_since_load_request = 0
local frames_after_tick0_written = 0

local frame_subscription = nil
local post_load_subscription = nil
local reset_subscription = nil

local breakpoint_ids = {}
local copied_hit_count = 0
local copied_lbpref_count = 0
local copied_lbrnd_count = 0
local output_opened_at_frame = -1
local last_errorlog_index = 0

local source_counts = {}
local random_r_low_counts = {}
local random_write_direction_counts = {}

local function h2(v) return string.format("%02X", v & 0xff) end
local function h4(v) return string.format("%04X", v & 0xffff) end
local function h1(v) return string.format("%X", v & 0x0f) end

local function output_path(suffix)
    local dir = CONFIG.output_dir or "."
    if dir == "" or dir == "." then
        return CONFIG.output_prefix .. suffix
    end

    local last = dir:sub(-1)
    if last == "/" or last == "\\" then
        return dir .. CONFIG.output_prefix .. suffix
    end

    return dir .. "/" .. CONFIG.output_prefix .. suffix
end

local function must_open(path, mode)
    local f, err = io.open(path, mode)
    if f == nil then
        error("Cannot open output file: " .. tostring(path) .. " / " .. tostring(err))
    end
    return f
end

local function open_outputs_if_needed()
    if hits_file ~= nil then return end

    hits_file = must_open(output_path("_preferred_pc_hits.log"), "w")
    summary_file = must_open(output_path("_preferred_pc_summary.txt"), "w")
    output_opened_at_frame = mame_frame

    hits_file:write("# Lady Bug preferred[] exact-PC diagnostic hits\n")
    hits_file:write("# v0.9.13c adds LBRND markers around 0x2EA3/0x2EA5 so the true LD A,R value can be captured.\n")
    hits_file:write("# LBPREF markers are kept for existing write-analysis compatibility: 2E97, 2EC7, 477D.\n")
    hits_file:write("# LBRND markers are additional context and should not be treated as preferred[] writes.\n")
    hits_file:write("# pollTick/mameFrame are Lua drain time, not exact CPU time.\n")
    hits_file:write("# The payload after LBPREF/LBRND is emitted at the exact breakpoint through MAME debugger logerror.\n")
    hits_file:write("# The launcher also enables -log; if this file misses lines, inspect tools/mame/lua/error.log.\n")
    hits_file:write(string.format("# output opened at mameFrame=%d\n", mame_frame))
    hits_file:flush()
end

local function close_outputs()
    if hits_file ~= nil then
        hits_file:flush()
        hits_file:close()
        hits_file = nil
    end

    if summary_file ~= nil then
        summary_file:flush()
        summary_file:close()
        summary_file = nil
    end
end

local function get_program_space()
    cpu = manager.machine.devices[":maincpu"]
    if cpu == nil then error("Cannot find device :maincpu") end

    mem = cpu.spaces["program"]
    if mem == nil then error("Cannot find :maincpu program address space") end

    machine_debugger = manager.machine.debugger
    if machine_debugger == nil then
        error("MAME debugger is not enabled. The launcher should add -debug automatically for this script.")
    end

    cpu_debug = cpu.debug
    if cpu_debug == nil then
        error(":maincpu debugger interface is unavailable.")
    end
end

local function read_u8(addr)
    return mem:read_u8(addr) & 0xff
end

local function preferred_snapshot_text()
    return string.format(
        "%02X,%02X,%02X,%02X",
        read_u8(0x61c4),
        read_u8(0x61c5),
        read_u8(0x61c6),
        read_u8(0x61c7))
end

local function chase_snapshot_text()
    return string.format(
        "%02X,%02X,%02X,%02X",
        read_u8(0x61ce),
        read_u8(0x61cf),
        read_u8(0x61d0),
        read_u8(0x61d1))
end

local function active_enemy_count()
    local count = 0
    local bases = { 0x602b, 0x6030, 0x6035, 0x603a }

    for _, base in ipairs(bases) do
        local raw = read_u8(base)
        if (raw & 0x02) ~= 0 then
            count = count + 1
        end
    end

    return count
end

local function inc_count(table_ref, key)
    key = tostring(key or "")
    if key == "" then key = "unknown" end
    table_ref[key] = (table_ref[key] or 0) + 1
end

local function parse_hex_field(line, key)
    local pattern = key .. "=([0-9A-Fa-f]+)"
    local text = line:match(pattern)
    if text == nil then return nil end
    return tonumber(text, 16)
end

local function observe_line(line)
    local marker = nil
    if line:find("LBPREF", 1, true) ~= nil then
        marker = "LBPREF"
        copied_lbpref_count = copied_lbpref_count + 1
    elseif line:find("LBRND", 1, true) ~= nil then
        marker = "LBRND"
        copied_lbrnd_count = copied_lbrnd_count + 1
    else
        return
    end

    copied_hit_count = copied_hit_count + 1

    local source = line:match("source=([^|]+)") or "unknown"
    inc_count(source_counts, marker .. ":" .. source)

    if source == "2EA5_PREF_RANDOM_R_VALUE" then
        local a = parse_hex_field(line, "a")
        if a ~= nil then inc_count(random_r_low_counts, h1(a)) end
    elseif source == "2EC7_RANDOM_WRITE" then
        local a = parse_hex_field(line, "a")
        if a ~= nil then inc_count(random_write_direction_counts, h2(a)) end
    end
end

local function is_preferred_diag_line(line)
    return line:find("LBPREF", 1, true) ~= nil or line:find("LBRND", 1, true) ~= nil
end

local function copy_new_debugger_log_lines(poll_tick)
    if hits_file == nil or machine_debugger == nil or machine_debugger.errorlog == nil then return end

    local n = #machine_debugger.errorlog

    -- Some MAME builds expose debugger.errorlog like a bounded visible buffer.
    -- If it appears to have wrapped or been cleared, restart from the beginning.
    if n < last_errorlog_index then
        last_errorlog_index = 0
    end

    for i = last_errorlog_index + 1, n do
        local line = tostring(machine_debugger.errorlog[i] or "")
        if is_preferred_diag_line(line) then
            observe_line(line)
            hits_file:write(
                string.format(
                    "pollTick=%d|mameFrame=%d|activeEnemies=%d|preferredNow=%s|chaseNow=%s|%s\n",
                    poll_tick,
                    mame_frame,
                    active_enemy_count(),
                    preferred_snapshot_text(),
                    chase_snapshot_text(),
                    line))
        end
    end

    last_errorlog_index = n

    if CONFIG.flush_every_trace_line then
        hits_file:flush()
    end
end

local function make_breakpoint_action(marker, label)
    -- The debugger action logs exact register/memory state, then continues with g.
    -- b@61C4..b@61C7 are the values before the current instruction executes.
    -- Extra fields are intentionally harmless for the existing preferred analyzer.
    return string.format(
        'logerror "%s|source=%s|pc=%%04X|r=%%02X|a=%%02X|b=%%02X|c=%%02X|d=%%02X|e=%%02X|h=%%02X|l=%%02X|hl=%%04X|de=%%04X|ix=%%04X|iy=%%04X|sp=%%04X|p0=%%02X|p1=%%02X|p2=%%02X|p3=%%02X|tmpDir=%%02X|tmpX=%%02X|tmpY=%%02X|rejected=%%02X|fallback=%%02X|chase0=%%02X|chase1=%%02X|chase2=%%02X|chase3=%%02X|rr=%%02X|timer61B9=%%02X|playerDir=%%02X|playerX=%%02X|playerY=%%02X|e0Raw=%%02X|e0X=%%02X|e0Y=%%02X|e1Raw=%%02X|e1X=%%02X|e1Y=%%02X|e2Raw=%%02X|e2X=%%02X|e2Y=%%02X|e3Raw=%%02X|e3X=%%02X|e3Y=%%02X\\n",pc,r,a,b,c,d,e,h,l,(h*256)+l,(d*256)+e,ix,iy,sp,b@61c4,b@61c5,b@61c6,b@61c7,b@61bd,b@61be,b@61bf,b@61c1,b@61c2,b@61ce,b@61cf,b@61d0,b@61d1,b@61d2,b@61b9,b@6198,b@6027,b@6028,b@602b,b@602c,b@602d,b@6030,b@6031,b@6032,b@6035,b@6036,b@6037,b@603a,b@603b,b@603c;g',
        marker,
        label)
end

local function set_breakpoint(addr, marker, label)
    local action = make_breakpoint_action(marker, label)

    local ok, bp_or_error = pcall(function()
        return cpu_debug:bpset(addr, "", action)
    end)

    if not ok then
        error("Could not set breakpoint " .. label .. " at " .. h4(addr) .. ": " .. tostring(bp_or_error))
    end

    breakpoint_ids[#breakpoint_ids + 1] = bp_or_error
    emu.print_info(string.format("Lady Bug preferred PC trace: breakpoint %s/%s at %04X -> #%s", marker, label, addr, tostring(bp_or_error)))
end

local function install_breakpoints()
    if #breakpoint_ids > 0 then return end

    if machine_debugger ~= nil and machine_debugger.errorlog ~= nil then
        last_errorlog_index = #machine_debugger.errorlog
    else
        last_errorlog_index = 0
    end

    -- Base preferred[] generator context.
    set_breakpoint(0x2e5c, "LBRND", "2E5C_PREF_ENTRY")
    set_breakpoint(0x2e6a, "LBRND", "2E6A_PREF_THRESHOLD_COMPARE")
    set_breakpoint(0x2e84, "LBRND", "2E84_PREF_INPUT_READ_RESULT")

    -- Rotate branch. The write remains LBPREF-compatible for existing analysis.
    set_breakpoint(0x2e8c, "LBRND", "2E8C_PREF_ROTATE_BRANCH")
    set_breakpoint(0x2e97, "LBPREF", "2E97_ROTATE_WRITE")

    -- Random branch. 0x2EA5 is the key: A contains the exact R value read by LD A,R.
    set_breakpoint(0x2e9e, "LBRND", "2E9E_PREF_RANDOM_BRANCH")
    set_breakpoint(0x2ea3, "LBRND", "2EA3_PREF_RANDOM_LOOP_BEFORE_LD_R")
    set_breakpoint(0x2ea5, "LBRND", "2EA5_PREF_RANDOM_R_VALUE")
    set_breakpoint(0x2ec7, "LBPREF", "2EC7_RANDOM_WRITE")

    -- BFS/chase override context. The write remains LBPREF-compatible.
    set_breakpoint(0x2ecb, "LBRND", "2ECB_PREF_CALL_BFS_OVERRIDE")
    set_breakpoint(0x46d8, "LBRND", "46D8_BFS_OVERRIDE_ENTRY")
    set_breakpoint(0x477d, "LBPREF", "477D_BFS_WRITE")

    -- Extra safety: if MAME is currently stopped in the debugger, resume.
    pcall(function()
        if machine_debugger.execution_state == "stop" then
            machine_debugger.execution_state = "run"
        end
    end)
    pcall(function() cpu_debug:go() end)
end

local function clear_breakpoints()
    if cpu_debug == nil then return end

    for _, id in ipairs(breakpoint_ids) do
        pcall(function()
            cpu_debug:bpclear(id)
        end)
    end

    breakpoint_ids = {}
end

local function sorted_pairs_by_count(counts)
    local pairs = {}
    for k, v in pairs(counts) do
        pairs[#pairs + 1] = { key = k, value = v }
    end
    table.sort(pairs, function(a, b)
        if a.value == b.value then return a.key < b.key end
        return a.value > b.value
    end)
    return pairs
end

local function write_counts(title, counts)
    summary_file:write(title .. "\n")
    local pairs = sorted_pairs_by_count(counts)
    if #pairs == 0 then
        summary_file:write("  none\n")
        return
    end

    for _, pair in ipairs(pairs) do
        summary_file:write(string.format("  %s=%d\n", pair.key, pair.value))
    end
end

local function write_summary(reason)
    if summary_file == nil then return end

    summary_file:write("Lady Bug preferred[] exact-PC diagnostic summary\n")
    summary_file:write("================================================\n\n")
    summary_file:write("version: v0.9.13c random/R-low exact-PC extension\n")
    summary_file:write("reason: " .. tostring(reason) .. "\n")
    summary_file:write("save_state: " .. tostring(CONFIG.save_state) .. "\n")
    summary_file:write("frames_after_tick0_to_capture: " .. tostring(CONFIG.frames_after_tick0_to_capture) .. "\n")
    summary_file:write("mame_frame: " .. tostring(mame_frame) .. "\n")
    summary_file:write("output_opened_at_frame: " .. tostring(output_opened_at_frame) .. "\n")
    summary_file:write("frames_after_tick0_written: " .. tostring(frames_after_tick0_written) .. "\n")
    summary_file:write("post_load_seen: " .. tostring(post_load_seen) .. "\n")
    summary_file:write("capture_started: " .. tostring(capture_started) .. "\n")
    summary_file:write("load_call_ok: " .. tostring(load_call_ok) .. "\n")
    summary_file:write("load_call_error: " .. tostring(load_call_error) .. "\n")
    summary_file:write("breakpoints_installed: " .. tostring(#breakpoint_ids) .. "\n")
    summary_file:write("copied_hit_count: " .. tostring(copied_hit_count) .. "\n")
    summary_file:write("copied_lbpref_count: " .. tostring(copied_lbpref_count) .. "\n")
    summary_file:write("copied_lbrnd_count: " .. tostring(copied_lbrnd_count) .. "\n")
    summary_file:write("\n")

    if mem ~= nil then
        summary_file:write("current_preferred: " .. preferred_snapshot_text() .. "\n")
        summary_file:write("current_chase: " .. chase_snapshot_text() .. "\n")
        summary_file:write("active_enemy_count: " .. tostring(active_enemy_count()) .. "\n")
        summary_file:write("\n")
    end

    write_counts("source_counts", source_counts)
    summary_file:write("\n")
    write_counts("random_true_R_low_counts_from_2EA5_A", random_r_low_counts)
    summary_file:write("\n")
    write_counts("random_write_direction_counts_from_2EC7_A", random_write_direction_counts)
    summary_file:write("\n")
    summary_file:write("notes:\n")
    summary_file:write("  - LBRND:2EA5_PREF_RANDOM_R_VALUE captures A immediately after LD A,R and before AND 0F.\n")
    summary_file:write("  - LBPREF:2EC7_RANDOM_WRITE captures the selected one-hot direction being written to preferred[].\n")
    summary_file:write("  - The existing C# preferred PC analyzer still reads LBPREF writes; LBRND lines are extra random/R-low context.\n")

    summary_file:flush()
end

local function finish(reason)
    if done then return end

    done = true
    copy_new_debugger_log_lines(frames_after_tick0_written)
    write_summary(reason)
    clear_breakpoints()
    close_outputs()

    emu.print_info("Lady Bug preferred PC trace finished: " .. tostring(reason))
    emu.print_info("Hit log: " .. output_path("_preferred_pc_hits.log"))
    emu.print_info("Summary: " .. output_path("_preferred_pc_summary.txt"))

    if CONFIG.pause_when_done then
        pcall(function() manager.machine:pause() end)
    end

    if CONFIG.exit_when_done then
        pcall(function() manager.machine:exit() end)
    end
end

local function start_capture_from_post_load()
    if capture_started then return end

    open_outputs_if_needed()
    install_breakpoints()

    post_load_seen = true
    capture_started = true
    frames_after_tick0_written = 0

    hits_file:write(string.format(
        "# tick0 post-load: mameFrame=%d preferred=%s chase=%s activeEnemies=%d\n",
        mame_frame,
        preferred_snapshot_text(),
        chase_snapshot_text(),
        active_enemy_count()))
    hits_file:flush()

    emu.print_info("Lady Bug preferred PC trace: capture started from post-load notifier.")
end

local function request_load_state()
    if load_requested then return end

    load_requested = true
    frames_since_load_request = 0

    emu.print_info("Lady Bug preferred PC trace: requesting state load '" .. tostring(CONFIG.save_state) .. "'")

    local ok, err = pcall(function()
        manager.machine:load(CONFIG.save_state)
    end)

    load_call_ok = ok
    if not ok then
        load_call_error = tostring(err)
        emu.print_error("Lady Bug preferred PC trace: state load request failed: " .. load_call_error)
        open_outputs_if_needed()
        write_summary("state load request failed")
        finish("state load request failed")
    end
end

local function on_frame_impl()
    if done then return end

    mame_frame = mame_frame + 1

    if not initialized then
        initialized = true
        get_program_space()
        open_outputs_if_needed()
        hits_file:write("# Lua script initialized; debugger interface available.\n")
        hits_file:flush()
        emu.print_info("Lady Bug preferred PC trace: initialized. Debugger is available.")
    end

    -- Extra safety in case the initial debugscript did not run or the debugger
    -- stopped before the Lua frame notifier could progress.
    pcall(function()
        if machine_debugger ~= nil and machine_debugger.execution_state == "stop" then
            machine_debugger.execution_state = "run"
        end
    end)

    if not load_requested then
        if mame_frame >= CONFIG.load_after_frames then
            request_load_state()
        end
        return
    end

    if not post_load_seen then
        frames_since_load_request = frames_since_load_request + 1

        -- Fallback: if post-load notifier is not called, avoid hanging forever.
        if frames_since_load_request > 240 then
            hits_file:write("# WARNING: post-load notifier did not fire; fallback capture started after delay.\n")
            hits_file:flush()
            install_breakpoints()
            post_load_seen = false
            capture_started = true
            frames_after_tick0_written = 0
        end

        return
    end

    if capture_started then
        copy_new_debugger_log_lines(frames_after_tick0_written)

        if frames_after_tick0_written % 60 == 0 then
            hits_file:write(string.format(
                "# heartbeat pollTick=%d mameFrame=%d copiedHitCount=%d LBPREF=%d LBRND=%d preferred=%s chase=%s\n",
                frames_after_tick0_written,
                mame_frame,
                copied_hit_count,
                copied_lbpref_count,
                copied_lbrnd_count,
                preferred_snapshot_text(),
                chase_snapshot_text()))
            hits_file:flush()
        end

        if frames_after_tick0_written >= CONFIG.frames_after_tick0_to_capture then
            finish("frame limit reached")
            return
        end

        frames_after_tick0_written = frames_after_tick0_written + 1
    end
end

local function on_frame()
    local ok, err = pcall(on_frame_impl)
    if not ok then
        open_outputs_if_needed()
        if hits_file ~= nil then
            hits_file:write("# ERROR in on_frame: " .. tostring(err) .. "\n")
            hits_file:flush()
        end
        emu.print_error("Lady Bug preferred PC trace: ERROR in on_frame: " .. tostring(err))
        finish("Lua error in on_frame")
    end
end

local function on_post_load_impl()
    if done then return end

    emu.print_info("Lady Bug preferred PC trace: post-load notifier fired.")
    start_capture_from_post_load()
end

local function on_post_load()
    local ok, err = pcall(on_post_load_impl)
    if not ok then
        open_outputs_if_needed()
        if hits_file ~= nil then
            hits_file:write("# ERROR in on_post_load: " .. tostring(err) .. "\n")
            hits_file:flush()
        end
        emu.print_error("Lady Bug preferred PC trace: ERROR in on_post_load: " .. tostring(err))
        finish("Lua error in on_post_load")
    end
end

local function on_reset()
    emu.print_info("Lady Bug preferred PC trace: machine reset observed.")
end

post_load_subscription = emu.add_machine_post_load_notifier(on_post_load)
reset_subscription = emu.add_machine_reset_notifier(on_reset)
frame_subscription = emu.add_machine_frame_notifier(on_frame)

emu.print_info("Lady Bug preferred PC trace script v0.9.13c loaded.")
