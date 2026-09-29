-- Small, local-only mpv bindings. They work while the embedded mpv child has focus.
local hold_timer = nil
local held = false
local prior_speed = 1.0

mp.add_forced_key_binding("SPACE", "youtube-scaler-space", function(e)
    if e.event == "down" then
        held = false
        prior_speed = mp.get_property_number("speed", 1.0)
        hold_timer = mp.add_timeout(0.18, function()
            held = true
            mp.set_property_number("speed", 2.0)
        end)
    elseif e.event == "up" then
        if hold_timer then hold_timer:kill(); hold_timer = nil end
        if held then mp.set_property_number("speed", prior_speed) else mp.command("cycle pause") end
    end
end, { complex = true })

mp.add_forced_key_binding("k", "toggle-pause", function() mp.command("cycle pause") end)
mp.add_forced_key_binding("j", "back-10", function() mp.commandv("seek", -10, "relative") end)
mp.add_forced_key_binding("l", "forward-10", function() mp.commandv("seek", 10, "relative") end)
mp.add_forced_key_binding("LEFT", "back-5", function() mp.commandv("seek", -5, "relative") end)
mp.add_forced_key_binding("RIGHT", "forward-5", function() mp.commandv("seek", 5, "relative") end)
mp.add_forced_key_binding("UP", "volume-up", function() mp.commandv("add", "volume", 5) end)
mp.add_forced_key_binding("DOWN", "volume-down", function() mp.commandv("add", "volume", -5) end)
mp.add_forced_key_binding("m", "mute", function() mp.command("cycle mute") end)
mp.add_forced_key_binding("0", "start", function() mp.commandv("seek", 0, "absolute") end)
for i = 1, 9 do
    mp.add_forced_key_binding(tostring(i), "percent-" .. i, function() mp.commandv("seek", i * 10, "absolute-percent") end)
end
mp.add_forced_key_binding("<", "slower", function() mp.commandv("multiply", "speed", 1 / 1.25) end)
mp.add_forced_key_binding(">", "faster", function() mp.commandv("multiply", "speed", 1.25) end)
