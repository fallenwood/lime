import { readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import type { Config } from "../utils/config.d.ts";
import { get_dict } from "../key_map/rime_dict.ts";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

let userConfig: Config | undefined;

try {
	const userConfigPath = "../user_config.ts";
	userConfig = (await import(userConfigPath)).default;
} catch {
	console.log("使用默认配置");
}

const config = userConfig || (await import("../config.ts")).default;

const filePath = path.join(__dirname, "preload_word.txt");

const { checkAddUserWord } = config.runner;

const words: string[] = [];
const args = process.argv.slice(2);

if (args[0] === "rime") {
	const p = args[1];
	const d = get_dict(p);
	for (const w of d) {
		const word = w.split("\t")[0].trim();
		const value = Number(w.split("\t")[2]?.trim() || "0");
		if (word && value > 5000) words.push(word);
	}
}

const oldWords = new Set<string>();
try {
	for (const x of readFileSync(filePath, "utf8").split("\n")) {
		if (x.trim()) oldWords.add(x.trim());
	}
} catch {
	// ignore
}

for (const [i, w] of words.entries()) {
	const res = await checkAddUserWord(w);
	if (res) oldWords.add(w);
	process.stdout.write(
		`预加载用户词 ${(((i + 1) / words.length) * 100).toFixed(2)}%\r`,
	);
}
console.log(`\n保存完毕`);

writeFileSync(filePath, Array.from(oldWords).join("\n"));
console.log("预加载用户词完成，数量", oldWords.size);
