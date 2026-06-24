import { createHash, randomBytes } from "node:crypto";
import { appendFile, mkdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const KEY_FILE = path.join(__dirname, "key.txt");

/**
 * 生成随机 key（URL-safe）并返回字符串。
 */
function generateKey(nBytes: number = 32): string {
	return randomBytes(nBytes).toString("base64url");
}

/**
 * 对 key 做 SHA-256 并返回 hex 哈希。
 */
async function hashKey(key: string): Promise<string> {
	return createHash("sha256").update(key).digest("hex");
}

/**
 * 将哈希追加到文件（每行一个），如果已存在则不重复写入。
 */
async function saveHash(
	hashHex: string,
	filePath: string = KEY_FILE,
): Promise<void> {
	// 读取已有哈希以避免重复
	const existing = new Set<string>();

	try {
		const content = await readFile(filePath, "utf8");
		content.split("\n").forEach((line) => {
			const trimmed = line.trim();
			if (trimmed) existing.add(trimmed);
		});
	} catch (error) {
		if (!isNotFoundError(error)) {
			throw error;
		}
		// 文件不存在是正常情况，继续执行
	}

	if (existing.has(hashHex)) {
		return;
	}

	// 确保目录存在
	await mkdir(path.dirname(filePath), { recursive: true });

	// 追加哈希到文件
	await appendFile(filePath, `${hashHex}\n`);
}

/**
 * 判断给定 key 的哈希是否存在于 key.txt 中。
 */
export async function verifyKey(
	key: string,
	filePath: string = KEY_FILE,
): Promise<boolean> {
	try {
		const hashHex = await hashKey(key);

		try {
			const content = await readFile(filePath, "utf8");
			return content.split("\n").some((line) => line.trim() === hashHex);
		} catch (error) {
			if (isNotFoundError(error)) {
				return false;
			}
			throw error;
		}
	} catch (error) {
		console.error("验证 key 失败:", error);
		return false;
	}
}

function isNotFoundError(error: unknown): boolean {
	return error instanceof Error && "code" in error && error.code === "ENOENT";
}

async function main(): Promise<void> {
	const { values: args } = parseArgs({
		args: process.argv.slice(2),
		options: {
			verify: { type: "string", short: "v" },
		},
	});

	if (args.verify) {
		const ok = await verifyKey(args.verify);
		if (ok) {
			console.log("Valid: key 的哈希存在于 key.txt。");
		} else {
			console.log("Invalid: key 的哈希不在 key.txt。");
		}
	} else {
		const key = generateKey();
		console.log("Generated key:", key);
		const h = await hashKey(key);
		await saveHash(h);
		console.log("Saved SHA-256 hash to", KEY_FILE);
	}
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
	main().catch((error) => {
		console.error(error);
		process.exit(1);
	});
}
