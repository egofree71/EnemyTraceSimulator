-- ladybug_enemywork_pc_trace.lua
--
-- Exact-PC MAME debugger trace for Lady Bug EnemyWork decision diagnostics.
--
-- v0.6.99 extends the 0x4130/0x3C0A diagnostics with exact-PC
-- breakpoints around the complete 0x427E decision gate.
--
-- Core decision breakpoints:
--   0x42CC : exact reset write to 61C1 / rejectedMask
--   0x42CF : exact reset write to 61C2 / fallback helper
--   0x4315 : exact rejectedMask OR write after rejected preferred candidate
--   0x4331 : exact rejectedMask OR write before fallback
--   0x43C4 : exact fallback helper increment
--   0x43C5 : fallback helper value after increment
--   0x3911 : logical maze direction validation entry
--   0x4130 : local door validation entry
--   0x4185 : local door accept return path
--   0x4187 : local door/tile rejection point
--   0x4241 : generic fallback entry point
--   0x4347 : forced reversal point
--   0x43D4 : commit temp state back to enemy slot
--
-- Release / den-exit breakpoints added in v0.6.91:
--   0x05AE : release helper entry
--   0x05B6 : release helper slot test loop
--   0x05C3 : call 0x3061 after finding a free enemy slot
--   0x05CC : post-init write (IX+0)=0x81 on the 0x05AE path
--   0x3061 : release slot init entry
--   0x3070 : write raw 0x82 into selected enemy slot
--   0x3074 : write X=0x58
--   0x3078 : write Y=0x86
--   0x3080 : sprite/attribute post-init write
--   0x4471 : alternate call path to 0x3061
--
-- Tile lookup breakpoint added in v0.6.94:
--   0x3C2B : return from GetTileUnderPlayerProbe / 0x3C0A.
--            At this point HL must contain:
--              0xD0A0 + ((D & 0xF8) * 4) + (E >> 3)
--            This formula intentionally preserves the arcade VRAM layout:
--            columns, bottom-to-top, not row-major screen order.
--
-- Local-door tile-value breakpoints added in v0.6.96:
--   0x4144 : immediately after 0x4143 LD A,(HL), down branch tile value in A
--   0x4157 : immediately after 0x4156 LD A,(HL), left branch tile value in A
--   0x416A : immediately after 0x4169 LD A,(HL), up branch tile value in A
--   0x417D : immediately after 0x417C LD A,(HL), right branch tile value in A
--

-- Decision-gate breakpoints added in v0.6.99:
--   0x42D2 : load temp dir/x/y before the decision gate call
--   0x42DA : call Enemy_IsAtDecisionCenter / 0x427E
--   0x427E : 0x427E entry, after A=tempDir and D/E=temp X/Y are loaded
--   0x428D : alignment passed; 0x427E is about to inspect direction geometry
--   0x4292 : horizontal-direction helper path inside 0x427E
--   0x429A : vertical-direction helper path inside 0x427E
--   0x42AA : helper compare path before carry/zero decision
--   0x42B6 : 0x427E returns carry set; caller should execute preferred decision
--   0x42B8 : 0x427E returns carry clear; caller should skip to forced-reversal path
--   0x42DD : caller branch point immediately after 0x427E
--   0x42E0 : preferred decision path was actually entered
--   0x433A : outside-center / forced-reversal path was actually entered
-- Important: Lady Bug's logical maze cell appears to be 16x16 pixels, i.e. 2x2
-- tiles. 0x3C0A/0x4130 still operate on individual 8x8 tile probes inside or
-- around that logical cell.
--
-- This is still a diagnostic, not gameplay simulation. Its job is to reveal the
-- exact order of source-PC events around release, fallback, commit, and tile probes.

local CONFIG = {
    output_prefix = "ladybug_sequence_v8_enemywork_pcdiag",
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
        emu.print_warning("Lady Bug EnemyWork PC trace: could not load " .. cfg_path .. ": " .. tostring(user_cfg))
        return
    end

    if type(user_cfg) ~= "table" then
        emu.print_warning("Lady Bug EnemyWork PC trace: " .. cfg_path .. " did not return a table")
        return
    end

    for k, v in pairs(user_cfg) do
        CONFIG[k] = v
    end

    emu.print_info("Lady Bug EnemyWork PC trace: loaded runtime config from " .. cfg_path)
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
local output_opened_at_frame = -1
local previous_lbew_lines = {}

local function h4(v) return string.format("%04X", v & 0xffff) end

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

    hits_file = must_open(output_path("_enemywork_pc_hits.log"), "w")
    summary_file = must_open(output_path("_enemywork_pc_summary.txt"), "w")
    output_opened_at_frame = mame_frame

    hits_file:write("# Lady Bug EnemyWork exact-PC diagnostic hits\n")
    hits_file:write("# v0.6.99 includes 0x427E decision-gate breakpoints, plus 0x4130 tile values and 0x3C0A address validation.\n")
    hits_file:write("# pollTick/mameFrame are Lua drain time, not exact CPU time.\n")
    hits_file:write("# LBEW payload is emitted at the exact breakpoint through MAME debugger logerror.\n")
    hits_file:write("# The launcher enables -log; inspect tools/mame/lua/error.log as primary raw output.\n")
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
    return string.format("%02X,%02X,%02X,%02X", read_u8(0x61c4), read_u8(0x61c5), read_u8(0x61c6), read_u8(0x61c7))
end

local function chase_snapshot_text()
    return string.format("%02X,%02X,%02X,%02X", read_u8(0x61ce), read_u8(0x61cf), read_u8(0x61d0), read_u8(0x61d1))
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

local function debugger_lbew_lines()
    local lines = {}

    if machine_debugger == nil or machine_debugger.errorlog == nil then
        return lines
    end

    local n = #machine_debugger.errorlog
    for i = 1, n do
        local line = tostring(machine_debugger.errorlog[i] or "")
        if line:find("LBEW", 1, true) ~= nil then
            lines[#lines + 1] = line
        end
    end

    return lines
end

local function make_set(lines)
    local set = {}
    for _, line in ipairs(lines) do
        set[line] = true
    end
    return set
end

local function copy_new_debugger_log_lines(poll_tick)
    if hits_file == nil then return end

    local current_lines = debugger_lbew_lines()
    local previous_set = make_set(previous_lbew_lines)

    for _, line in ipairs(current_lines) do
        if not previous_set[line] then
            hits_file:write(string.format(
                "pollTick=%d|mameFrame=%d|activeEnemies=%d|preferredNow=%s|chaseNow=%s|rejectedNow=%02X|fallbackNow=%02X|%s\n",
                poll_tick,
                mame_frame,
                active_enemy_count(),
                preferred_snapshot_text(),
                chase_snapshot_text(),
                read_u8(0x61c1),
                read_u8(0x61c2),
                line))
            copied_hit_count = copied_hit_count + 1
        end
    end

    previous_lbew_lines = current_lines

    if CONFIG.flush_every_trace_line then
        hits_file:flush()
    end
end

local function set_breakpoint(addr, label)
    -- Exact-PC diagnostic action.
    -- The debugger action cannot call Lua helpers at breakpoint time, so the C#
    -- analyzer derives active enemy count from e0..e3 raw bytes.
    local action = string.format(
        'logerror "LBEW|source=%s|pc=%%04X|r=%%02X|a=%%02X|b=%%02X|c=%%02X|d=%%02X|e=%%02X|h=%%02X|l=%%02X|hl=%%04X|de=%%04X|ix=%%04X|iy=%%04X|sp=%%04X|p0=%%02X|p1=%%02X|p2=%%02X|p3=%%02X|tmpDir=%%02X|tmpX=%%02X|tmpY=%%02X|rejected=%%02X|fallback=%%02X|chase0=%%02X|chase1=%%02X|chase2=%%02X|chase3=%%02X|rr=%%02X|playerDir=%%02X|playerX=%%02X|playerY=%%02X|e0Raw=%%02X|e0X=%%02X|e0Y=%%02X|e1Raw=%%02X|e1X=%%02X|e1Y=%%02X|e2Raw=%%02X|e2X=%%02X|e2Y=%%02X|e3Raw=%%02X|e3X=%%02X|e3Y=%%02X\\n",pc,r,a,b,c,d,e,h,l,(h*256)+l,(d*256)+e,ix,iy,sp,b@61c4,b@61c5,b@61c6,b@61c7,b@61bd,b@61be,b@61bf,b@61c1,b@61c2,b@61ce,b@61cf,b@61d0,b@61d1,b@61d2,b@6198,b@6027,b@6028,b@602b,b@602c,b@602d,b@6030,b@6031,b@6032,b@6035,b@6036,b@6037,b@603a,b@603b,b@603c;g',
        label)

    local ok, bp_or_error = pcall(function()
        return cpu_debug:bpset(addr, "", action)
    end)

    if not ok then
        error("Could not set breakpoint " .. label .. " at " .. h4(addr) .. ": " .. tostring(bp_or_error))
    end

    breakpoint_ids[#breakpoint_ids + 1] = bp_or_error
    emu.print_info(string.format("Lady Bug EnemyWork PC trace: breakpoint %s at %04X -> #%s", label, addr, tostring(bp_or_error)))
end

local function install_breakpoints()
    if #breakpoint_ids > 0 then return end

    previous_lbew_lines = debugger_lbew_lines()

    -- Release / den-exit context. These are intentionally before the generic
    -- Enemy_UpdateOne breakpoints in the log so the startup sequence is easy to read.
    set_breakpoint(0x05ae, "05AE_RELEASE_HELPER_ENTRY")
    set_breakpoint(0x05b6, "05B6_RELEASE_SLOT_TEST")
    set_breakpoint(0x05c3, "05C3_RELEASE_CALL_3061")
    set_breakpoint(0x05cc, "05CC_RELEASE_RAW81_WRITE")
    set_breakpoint(0x3061, "3061_RELEASE_INIT_ENTRY")
    set_breakpoint(0x3070, "3070_RELEASE_INIT_RAW82")
    set_breakpoint(0x3074, "3074_RELEASE_INIT_X58")
    set_breakpoint(0x3078, "3078_RELEASE_INIT_Y86")
    set_breakpoint(0x3080, "3080_RELEASE_INIT_SPRITE")
    set_breakpoint(0x4471, "4471_ALT_RELEASE_CALL_3061")

    -- Exact writes to rejectedMask / fallback helper.
    set_breakpoint(0x42cc, "42CC_REJECT_RESET")
    set_breakpoint(0x42cf, "42CF_FALLBACK_RESET")
    set_breakpoint(0x4315, "4315_REJECT_OR_CANDIDATE")
    set_breakpoint(0x4331, "4331_REJECT_OR_TEMPDIR")
    set_breakpoint(0x43c4, "43C4_FALLBACK_STEP_INC")
    set_breakpoint(0x43c5, "43C5_FALLBACK_STEP_READ")

    -- 0x427E decision gate around Enemy_UpdateOne.
    -- This is intentionally source-first diagnostic instrumentation: the code
    -- is more than the simple alignment predicate currently used by the C#
    -- shadow model. The caller enters 0x42E6 only if 0x427E returns carry set;
    -- otherwise it jumps to 0x433A forced-reversal / plain movement path.
    set_breakpoint(0x42d2, "42D2_LOAD_TEMP_BEFORE_DECISION_GATE")
    set_breakpoint(0x42da, "42DA_CALL_427E_DECISION_GATE")
    set_breakpoint(0x427e, "427E_DECISION_GATE_ENTRY")
    set_breakpoint(0x428d, "428D_DECISION_GATE_ALIGNMENT_PASSED")
    set_breakpoint(0x4292, "4292_DECISION_GATE_HORIZONTAL_HELPER")
    set_breakpoint(0x429a, "429A_DECISION_GATE_VERTICAL_HELPER")
    set_breakpoint(0x42aa, "42AA_DECISION_GATE_HELPER_COMPARE")
    set_breakpoint(0x42b6, "42B6_DECISION_GATE_RET_CARRY_SET")
    set_breakpoint(0x42b8, "42B8_DECISION_GATE_RET_CARRY_CLEAR")
    set_breakpoint(0x42dd, "42DD_AFTER_427E_BRANCH_POINT")
    set_breakpoint(0x42e0, "42E0_ENTER_PREFERRED_DECISION")
    set_breakpoint(0x433a, "433A_ENTER_OUTSIDE_CENTER_PATH")

    -- 0x3C0A returns with HL pointing at the selected VRAM tile.
    -- This validates the source address formula but not the tile value.
    set_breakpoint(0x3c2b, "3C2B_TILE_LOOKUP_RETURN")

    -- 0x4130 local-door tile value reads. We stop at the following CP
    -- instruction so A already contains the value loaded by LD A,(HL).
    set_breakpoint(0x4144, "4144_AFTER_4143_TILE_READ_DOWN")
    set_breakpoint(0x4157, "4157_AFTER_4156_TILE_READ_LEFT")
    set_breakpoint(0x416a, "416A_AFTER_4169_TILE_READ_UP")
    set_breakpoint(0x417d, "417D_AFTER_417C_TILE_READ_RIGHT")
    set_breakpoint(0x4185, "4185_LOCAL_DOOR_ACCEPT")

    -- Context around validation / fallback / forced reversal / commit.
    set_breakpoint(0x3911, "3911_LOGICAL_MAZE_VALIDATE")
    set_breakpoint(0x4130, "4130_LOCAL_DOOR_CHECK_ENTRY")
    set_breakpoint(0x4187, "4187_LOCAL_DOOR_REJECT")
    set_breakpoint(0x4241, "4241_FALLBACK_ENTRY")
    set_breakpoint(0x4347, "4347_FORCED_REVERSAL")
    set_breakpoint(0x43d4, "43D4_COMMIT_TEMP_STATE")

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

local function write_summary(reason)
    if summary_file == nil then return end

    summary_file:write("Lady Bug EnemyWork exact-PC diagnostic summary\n")
    summary_file:write("============================================\n\n")
    summary_file:write("version: v0.6.99 0x427E decision-gate breakpoint extension\n")
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

    if mem ~= nil then
        summary_file:write("current_preferred: " .. preferred_snapshot_text() .. "\n")
        summary_file:write("current_chase: " .. chase_snapshot_text() .. "\n")
        summary_file:write(string.format("current_rejected: %02X\n", read_u8(0x61c1)))
        summary_file:write(string.format("current_fallback: %02X\n", read_u8(0x61c2)))
        summary_file:write("active_enemy_count: " .. tostring(active_enemy_count()) .. "\n")
    end

    summary_file:flush()
end

local function finish(reason)
    if done then return end

    done = true
    copy_new_debugger_log_lines(frames_after_tick0_written)
    write_summary(reason)
    clear_breakpoints()
    close_outputs()

    emu.print_info("Lady Bug EnemyWork PC trace finished: " .. tostring(reason))
    emu.print_info("Hit log: " .. output_path("_enemywork_pc_hits.log"))
    emu.print_info("Summary: " .. output_path("_enemywork_pc_summary.txt"))

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
        "# tick0 post-load: mameFrame=%d preferred=%s chase=%s rejected=%02X fallback=%02X activeEnemies=%d\n",
        mame_frame,
        preferred_snapshot_text(),
        chase_snapshot_text(),
        read_u8(0x61c1),
        read_u8(0x61c2),
        active_enemy_count()))
    hits_file:flush()

    emu.print_info("Lady Bug EnemyWork PC trace: capture started from post-load notifier.")
end

local function request_load_state()
    if load_requested then return end

    load_requested = true
    frames_since_load_request = 0

    emu.print_info("Lady Bug EnemyWork PC trace: requesting state load '" .. tostring(CONFIG.save_state) .. "'")

    local ok, err = pcall(function()
        manager.machine:load(CONFIG.save_state)
    end)

    load_call_ok = ok
    if not ok then
        load_call_error = tostring(err)
        emu.print_error("Lady Bug EnemyWork PC trace: state load request failed: " .. load_call_error)
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
        emu.print_info("Lady Bug EnemyWork PC trace: initialized. Debugger is available.")
    end

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
                "# heartbeat pollTick=%d mameFrame=%d copiedHitCount=%d preferred=%s rejected=%02X fallback=%02X\n",
                frames_after_tick0_written,
                mame_frame,
                copied_hit_count,
                preferred_snapshot_text(),
                read_u8(0x61c1),
                read_u8(0x61c2)))
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
        emu.print_error("Lady Bug EnemyWork PC trace: ERROR in on_frame: " .. tostring(err))
        finish("Lua error in on_frame")
    end
end

local function on_post_load_impl()
    if done then return end

    emu.print_info("Lady Bug EnemyWork PC trace: post-load notifier fired.")
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
        emu.print_error("Lady Bug EnemyWork PC trace: ERROR in on_post_load: " .. tostring(err))
        finish("Lua error in on_post_load")
    end
end

local function on_reset()
    emu.print_info("Lady Bug EnemyWork PC trace: machine reset observed.")
end

post_load_subscription = emu.add_machine_post_load_notifier(on_post_load)
reset_subscription = emu.add_machine_reset_notifier(on_reset)
frame_subscription = emu.add_machine_frame_notifier(on_frame)

emu.print_info("Lady Bug EnemyWork PC trace script loaded, v0.6.99 0x427E decision-gate breakpoint extension.")
