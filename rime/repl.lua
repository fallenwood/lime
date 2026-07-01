local script_path = arg and arg[0] or "repl.lua"
local script_dir = script_path:match("^(.*[/\\])") or ""
local lua_dir = script_dir .. "lua"

package.path = table.concat({
  lua_dir .. "/?.lua",
  lua_dir .. "/?/init.lua",
  package.path,
}, ";")

local llm_pinyin = require("llm_pinyin")
local translator = llm_pinyin.translator
local yielded_candidates = {}

function Candidate(candidate_type, start_pos, end_pos, text, comment)
  return {
    type = candidate_type,
    start = start_pos,
    _end = end_pos,
    text = text,
    comment = comment,
  }
end

function yield(candidate)
  yielded_candidates[#yielded_candidates + 1] = candidate
end

local env = {
  engine = {
    context = {
      get_preedit = function()
        return { text = "" }
      end,
    },
  },
}

local function print_candidates(input)
  yielded_candidates = {}
  local seg = {
    start = 0,
    _end = #input,
  }

  local ok, error_message = pcall(translator.func, input, seg, env)
  if not ok then
    io.stderr:write("error: " .. tostring(error_message) .. "\n")
    return false
  end

  if #yielded_candidates == 0 then
    print("(no candidates)")
    return true
  end

  for index, candidate in ipairs(yielded_candidates) do
    local preedit = candidate.preedit and (" [" .. candidate.preedit .. "]") or ""
    print(string.format(
      "%d. %s%s\tquality=%s\tspan=%d-%d",
      index,
      candidate.text,
      preedit,
      tostring(candidate.quality),
      candidate.start,
      candidate._end
    ))
  end

  return true
end

local function run_once(args)
  local had_error = false
  for index = 1, #args do
    local input = args[index]
    if #args > 1 then
      print("> " .. input)
    end
    if not print_candidates(input) then
      had_error = true
    end
  end

  if had_error then
    os.exit(1)
  end
end

local function run_repl()
  print("LIME llm_pinyin REPL")
  print("Type pinyin keys and press Enter. Empty input, :q, or :quit exits.")

  while true do
    io.write("lime> ")
    local input = io.read("*l")
    if not input or input == "" or input == ":q" or input == ":quit" then
      break
    end
    print_candidates(input)
  end
end

if arg and #arg > 0 then
  run_once(arg)
else
  run_repl()
end