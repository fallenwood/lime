import { readFileSync } from "node:fs";
import { createServer as createNamedPipeServer, type Socket } from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import type { Config } from "./utils/config.d.ts";

let userConfig: Config | undefined;

try {
	const userConfigPath = "./user_config.ts";
	userConfig = (await import(userConfigPath)).default;
} catch {
	console.log("使用默认配置");
}

const config = userConfig || (await import("./config.ts")).default;

const { single_ci, commit, getUserData, addUserWord } = config.runner;

function arrayLimtPush<T>(arr: T[], item: T, maxLen: number) {
	arr.push(item);
	if (arr.length <= maxLen) return;
	for (let i = 0; i < arr.length - maxLen; i++) {
		arr.shift();
	}
}

const defaultNamedPipeName = "lime";
const windowsNamedPipePrefix = "\\\\.\\pipe\\";

function getNamedPipePath(pipe: string) {
	if (process.platform !== "win32") {
		throw new Error("当前仅支持 Windows 命名管道");
	}

	const normalizedPipe = pipe.trim() || defaultNamedPipeName;
	if (normalizedPipe.startsWith(windowsNamedPipePrefix)) {
		return normalizedPipe;
	}
	return `${windowsNamedPipePrefix}${normalizedPipe}`;
}

function serveNamedPipe(pipe: string) {
	const pipePath = getNamedPipePath(pipe);

	const server = createNamedPipeServer((socket) => {
		handleRpcSocket(socket);
	});
	server.on("error", (error) => {
		console.error(`命名管道启动失败：${pipePath}`);
		console.error(error);
		process.exitCode = 1;
	});
	server.listen(pipePath, () => {
		console.log(`命名管道已启动：${pipePath}`);
	});
	return server;
}

type RpcRequest = {
	action?: string;
	body?: unknown;
};

type RpcResponse = {
	status: number;
	body: string;
};

class RpcError extends Error {
	readonly status: number;

	constructor(status: number, message: string) {
		super(message);
		this.status = status;
	}
}

function rpcJson(status: number, value: unknown): RpcResponse {
	return {
		status,
		body: typeof value === "string" ? value : JSON.stringify(value),
	};
}

function parseRpcBody<T>(body: unknown, fallback: T): T {
	if (typeof body !== "string") return (body ?? fallback) as T;
	if (!body) return fallback;
	return JSON.parse(body) as T;
}

function handleRpcSocket(socket: Socket) {
	let buffer = "";
	let handled = false;

	socket.setEncoding("utf8");
	socket.on("data", (chunk) => {
		if (handled) return;
		buffer += chunk;
		const newline = buffer.indexOf("\n");
		if (newline === -1) return;

		handled = true;
		socket.pause();
		void respondToRpcLine(socket, buffer.slice(0, newline));
	});
	socket.on("error", (error) => {
		console.error("命名管道连接错误:", error);
	});
}

async function respondToRpcLine(socket: Socket, line: string) {
	let response: RpcResponse;
	try {
		const request = JSON.parse(line) as RpcRequest;
		response = await handleRpcRequest(request);
	} catch (error) {
		if (error instanceof RpcError) {
			response = rpcJson(error.status, { message: error.message });
		} else {
			console.error("处理命名管道请求失败:", error);
			response = rpcJson(500, { message: "命名管道请求处理失败" });
		}
	}

	socket.end(`${JSON.stringify(response)}\n`);
}

const inputLogMaxLen = 10 ** 5;
export const inputLog: {
	keyDeltaTimes: Array<number>;
	lastKeyTime: number | null;
	ziDeltaTimes: Array<number>;
	lastZiTime: number | null;
	ziCount: number;
	lastCandidates: {
		time: number;
		candidates: string[];
	};
	offsetTimes: Record<number, Array<number>>;
	history: string; // 与直接从模型获取记录不同，模型有上下文限制，这里记录所有输入的文本，供后续微调使用
} = {
	keyDeltaTimes: [],
	lastKeyTime: null,
	ziDeltaTimes: [],
	lastZiTime: null,
	ziCount: 0,
	lastCandidates: {
		time: 0,
		candidates: [],
	},
	offsetTimes: {},
	history: "",
};

try {
	const words = readFileSync(config.userWordsPath, "utf8")
		.split("\n")
		.filter((w) => w.trim());
	for (const [i, w] of words.entries()) {
		addUserWord(w);
		process.stdout.write(
			`加载用户词 ${(((i + 1) / words.length) * 100).toFixed(2)}%\r`,
		);
	}
	console.log(`\n加载用户词完成，数量 ${words.length}`);
} catch {
	//
}

async function handleCandidates(body: unknown) {
	const data = parseRpcBody<{ keys?: string }>(body, {});
	const keys = data.keys || "";

	console.log(keys);
	const time = Date.now();
	if (inputLog.lastKeyTime === null || keys.length === 1) {
		inputLog.lastKeyTime = time;
		inputLog.lastZiTime = time;
	} else {
		arrayLimtPush(
			inputLog.keyDeltaTimes,
			time - inputLog.lastKeyTime,
			inputLogMaxLen,
		);
		inputLog.lastKeyTime = time;
	}

	const pinyinInput = config.key2ZiInd(keys);
	const result = await single_ci(pinyinInput);

	if (result.candidates.length <= 1) {
		inputLog.lastZiTime = null;
	} else
		inputLog.lastCandidates = {
			time,
			candidates: result.candidates.map((c) => c.word),
		};

	return result;
}

async function handleCommit(body: unknown) {
	let data: { text?: string; new?: boolean; update?: boolean };
	try {
		data = parseRpcBody(body, {});
	} catch (error) {
		console.error("提交文本失败:", error);
		throw new RpcError(400, "请求数据格式错误");
	}

	const text = data.text || "";
	const isNew = data.new ?? true;
	const shouldUpdate = data.update ?? false;

	if (!text) {
		throw new RpcError(400, "未提供文本内容");
	}

	const newT = await commit(text, shouldUpdate, isNew);

	if (isNew) {
		if (inputLog.lastZiTime !== null)
			arrayLimtPush(
				inputLog.ziDeltaTimes,
				(Date.now() - inputLog.lastZiTime) / text.length,
				inputLogMaxLen,
			);
		inputLog.lastZiTime = null;
		inputLog.lastKeyTime = null;
		inputLog.ziCount += text.length;
		inputLog.history += text;
	}
	{
		const offset = inputLog.lastCandidates.candidates.indexOf(newT ?? "");
		if (offset !== -1 && inputLog.lastCandidates.time !== 0) {
			const time = Date.now();
			const ofts = inputLog.offsetTimes[offset] || [];
			arrayLimtPush(
				ofts,
				time - inputLog.lastCandidates.time,
				inputLogMaxLen,
			);
			inputLog.offsetTimes[offset] = ofts;
		}
		inputLog.lastCandidates = {
			time: 0,
			candidates: [],
		};
	}

	return {
		message: "文本提交成功",
	};
}

async function handleLearnText(body: unknown) {
	const text = typeof body === "string" ? body : String(body ?? "");
	await commit(text, true, true);
	return {
		message: "文本提交成功",
	};
}

async function handleRpcRequest(request: RpcRequest) {
	switch (request.action) {
		case "candidates":
			return rpcJson(200, await handleCandidates(request.body));
		case "commit":
			return rpcJson(200, await handleCommit(request.body));
		case "userdata":
			return rpcJson(200, getUserData());
		case "inputlog":
			return rpcJson(200, inputLog);
		case "learntext":
			return rpcJson(200, await handleLearnText(request.body));
		default:
			throw new RpcError(404, `未知命名管道操作：${String(request.action)}`);
	}
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
	const { values } = parseArgs({
		args: process.argv.slice(2),
		options: {
			pipe: { type: "string" },
		},
	});
	serveNamedPipe(String(values.pipe ?? process.env.LIME_PIPE ?? defaultNamedPipeName));
}
