---@class FetchOptions
---@field source? string 请求体内容，通常是 JSON 字符串
---@field timeout? number 超时时间（秒）
---@field pipe string Windows 命名管道路径
---@field [string] any 其他可能的选项

local json = require("json")

local is_windows = package.config:sub(1, 1) == "\\"

local function action_name(name)
    return name:gsub("^/+", "")
end

local function fetch_pipe(action, op)
    if not is_windows then
        return nil, "当前文件读写命名管道客户端仅支持 Windows 命名管道"
    end

    local request = json.encode({
        action = action_name(action),
        body = op.source or "",
    }) .. "\n"

    local file, open_err = io.open(op.pipe, "r+b")
    if not file then
        return nil, "无法打开命名管道：" .. tostring(open_err)
    end

    local ok, write_err = file:write(request)
    if ok then
        ok, write_err = file:flush()
    end
    if not ok then
        file:close()
        return nil, "写入命名管道失败：" .. tostring(write_err)
    end

    local line, read_err = file:read("*l")

    file:close()
    if not line then
        return nil, "读取命名管道响应失败：" .. tostring(read_err)
    end

    local ok, response = pcall(json.decode, line)
    if not ok or type(response) ~= "table" then
        return nil, "无效的命名管道响应"
    end

    return tonumber(response.status), response.body
end

---通过命名管道发送请求并获取响应
---@param action string 操作名称
---@param op FetchOptions 请求选项
---@return integer? status_code 状态码，失败时为nil
---@return string|nil response_body 响应体字符串，失败时为错误信息
local function fetch_text(action, op)
    op = op or {}
    if not op.pipe or op.pipe == "" then
        return nil, "未配置命名管道"
    end
    return fetch_pipe(action, op)
end

return fetch_text
