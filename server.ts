import { createAdaptorServer } from "@hono/node-server";
import { lstatSync, readFileSync, unlinkSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { Hono } from "hono";
import { HTTPException } from "hono/http-exception";
import { logger } from "hono/logger";
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
	const normalizedPipe = pipe.trim() || defaultNamedPipeName;
	if (process.platform === "win32") {
		if (normalizedPipe.startsWith(windowsNamedPipePrefix)) {
			return normalizedPipe;
		}
		return `${windowsNamedPipePrefix}${normalizedPipe}`;
	}
	if (path.isAbsolute(normalizedPipe)) return normalizedPipe;
	return path.join(
		tmpdir(),
		normalizedPipe.endsWith(".sock")
			? normalizedPipe
			: `${normalizedPipe}.sock`,
	);
}

function removeStaleUnixSocket(socketPath: string) {
	if (process.platform === "win32") return;
	try {
		const stats = lstatSync(socketPath);
		if (!stats.isSocket()) {
			throw new Error(`命名管道路径已存在且不是 socket：${socketPath}`);
		}
		unlinkSync(socketPath);
	} catch (error) {
		if ((error as NodeJS.ErrnoException).code === "ENOENT") return;
		throw error;
	}
}

function serveNamedPipe(pipe: string) {
	const pipePath = getNamedPipePath(pipe);
	removeStaleUnixSocket(pipePath);

	const server = createAdaptorServer({ fetch: app.fetch });
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

const app = new Hono();
const api = new Hono();

api.use("*", logger());

api.post("/candidates", async (c) => {
	const body = await c.req.json<{ keys?: string }>();
	const keys = body.keys || "";

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

	return c.json(result);
});

api.post("/commit", async (c) => {
	try {
		const body = await c.req.json();
		const text = body.text || "";
		const isNew = body.new ?? true;
		const shouldUpdate = body.update ?? false;

		if (!text) {
			throw new HTTPException(400, { message: "未提供文本内容" });
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

		return c.json({
			message: "文本提交成功",
		});
	} catch (error) {
		if (error instanceof HTTPException) throw error;
		console.error("提交文本失败:", error);
		throw new HTTPException(400, { message: "请求数据格式错误" });
	}
});

api.get("/userdata", (c) => {
	return c.json(getUserData());
});

api.get("/inputlog", (c) => {
	return c.json(inputLog);
});

api.post("/learntext", async (c) => {
	const body = await c.req.text();
	await commit(body, true, true);
	return c.json({
		message: "文本提交成功",
	});
});

app.route("/api", api);

app.post("/candidates", (c) => {
	return api.fetch(c.req.raw);
});

app.post("/commit", (c) => {
	return api.fetch(c.req.raw);
});

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
	const { values } = parseArgs({
		args: process.argv.slice(2),
		options: {
			pipe: { type: "string" },
		},
	});
	serveNamedPipe(String(values.pipe ?? process.env.LIME_PIPE ?? defaultNamedPipeName));
}

export default app;
