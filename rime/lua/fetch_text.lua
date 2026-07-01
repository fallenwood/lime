---@alias HttpMethod '"GET"'|'"POST"'|'"PUT"'|'"DELETE"'|'"PATCH"'|'"HEAD"'|'"OPTIONS"'

---@class FetchOptions
---@field method? HttpMethod HTTP请求方法，默认为"GET"
---@field headers? table<string, string> 请求头，键值对形式
---@field source? string 请求体内容
---@field timeout? number 超时时间（秒）
---@field pipe string 命名管道或 Unix socket 路径
---@field [string] any 其他可能的选项

local is_windows = package.config:sub(1, 1) == "\\"
local read_buffer_size = 8192

local function url_path(url)
    local path = url:match("^https?://[^/]*(/.*)$") or url
    if path == "" then
        return "/"
    end
    if path:sub(1, 1) ~= "/" then
        return "/" .. path
    end
    return path
end

local function build_http_request(url, op)
    local body = op.source or ""
    local header_lines = {
        "Host: lime",
        "Connection: close",
        "Content-Length: " .. tostring(#body),
    }

    if op.headers then
        for key, value in pairs(op.headers) do
            local lower_key = tostring(key):lower()
            if lower_key ~= "host" and lower_key ~= "connection" and lower_key ~= "content-length" then
                header_lines[#header_lines + 1] = tostring(key) .. ": " .. tostring(value)
            end
        end
    end

    return table.concat({
        (op.method or "GET") .. " " .. url_path(url) .. " HTTP/1.1",
        table.concat(header_lines, "\r\n"),
        "",
        body,
    }, "\r\n")
end

local function decode_chunked(body)
    local chunks = {}
    local pos = 1

    while true do
        local line_end = body:find("\r\n", pos, true)
        if not line_end then
            return nil, false
        end

        local size_text = body:sub(pos, line_end - 1):match("^%s*([^;]+)")
        local size = tonumber(size_text, 16)
        if not size then
            return nil, true, "无效的 chunked 响应"
        end

        pos = line_end + 2
        if #body < pos + size + 1 then
            return nil, false
        end

        chunks[#chunks + 1] = body:sub(pos, pos + size - 1)
        pos = pos + size

        if body:sub(pos, pos + 1) ~= "\r\n" then
            return nil, false
        end
        pos = pos + 2

        if size == 0 then
            return table.concat(chunks), true
        end
    end
end

local function parse_response(raw, eof)
    local header_end = raw:find("\r\n\r\n", 1, true)
    if not header_end then
        if eof then
            return nil, nil, true, "响应头不完整"
        end
        return nil, nil, false
    end

    local header_text = raw:sub(1, header_end - 1)
    local body = raw:sub(header_end + 4)
    local status = tonumber(header_text:match("^HTTP/%d%.%d%s+(%d%d%d)"))
    if not status then
        return nil, nil, true, "无效的响应状态"
    end

    local headers = {}
    for line in header_text:gmatch("[^\r\n]+") do
        local key, value = line:match("^([^:]+):%s*(.*)$")
        if key then
            headers[key:lower()] = value
        end
    end

    local transfer_encoding = headers["transfer-encoding"]
    if transfer_encoding and transfer_encoding:lower():find("chunked", 1, true) then
        local decoded, complete, err = decode_chunked(body)
        if decoded then
            return status, decoded, true
        end
        if err then
            return nil, nil, true, err
        end
        if eof then
            return nil, nil, true, "响应分块不完整"
        end
        return nil, nil, false
    end

    local content_length = tonumber(headers["content-length"])
    if content_length then
        if #body >= content_length then
            return status, body:sub(1, content_length), true
        end
        if eof then
            return nil, nil, true, "响应内容不完整"
        end
        return nil, nil, false
    end

    if eof then
        return status, body, true
    end
    return nil, nil, false
end

local function read_response(read_chunk)
    local chunks = {}

    while true do
        local chunk, err, eof = read_chunk()
        if chunk and chunk ~= "" then
            chunks[#chunks + 1] = chunk
        end

        local status, body, done, parse_err = parse_response(table.concat(chunks), eof)
        if done then
            if parse_err then
                return nil, parse_err
            end
            return status, body
        end
        if err then
            return nil, err
        end
    end
end

local function fetch_pipe(url, op)
    local request = build_http_request(url, op)
    if not is_windows then
        return nil, "当前文件读写命名管道客户端仅支持 Windows 命名管道"
    end

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

    local status, body = read_response(function()
        local chunk, read_err = file:read(read_buffer_size)
        if chunk then
            return chunk
        end
        return nil, read_err, true
    end)

    file:close()
    return status, body
end

---通过命名管道发送请求并获取响应
---@param url string 请求的URL
---@param op FetchOptions 请求选项
---@return integer? status_code HTTP状态码，失败时为nil
---@return string|nil response_body 响应体字符串，失败时为错误信息
local function fetch_text(url, op)
    op = op or {}
    if not op.pipe or op.pipe == "" then
        return nil, "未配置命名管道"
    end
    return fetch_pipe(url, op)
end

return fetch_text
