import { deepStrictEqual, ok } from "node:assert/strict";
import { test } from "node:test";
import { generate_fuzzy_pinyin } from "../fuzzy_pinyin.ts";
import { load_pinyin } from "../gen_zi_pinyin.ts";
import { keys_to_pinyin } from "../keys_to_pinyin.ts";
import { spilt_pinyin } from "../split_pinyin.ts";

test("split pinyin", () => {
	deepStrictEqual(spilt_pinyin("ni"), ["n", "i"]);
	deepStrictEqual(spilt_pinyin("zhe"), ["zh", "e"]);
	deepStrictEqual(spilt_pinyin("zhang"), ["zh", "ang"]);
});

test("fuzzy pinyin", () => {
	deepStrictEqual(
		new Set(generate_fuzzy_pinyin("shang")),
		new Set(["shang", "shan", "san", "sang"]),
	);
	deepStrictEqual(
		new Set(generate_fuzzy_pinyin("er", { all: { er: "e", e: "er" } })),
		new Set(["er", "e"]),
	);
});

test("拼音 常规", () => {
	const x = keys_to_pinyin("nihao");
	deepStrictEqual(x, [
		[{ ind: "ni", key: "ni", preeditShow: "ni" }],
		[{ ind: "hao", key: "hao", preeditShow: "hao" }],
	]);
});

test("拼音 部分", () => {
	const x = keys_to_pinyin("nihaow").at(-1);
	ok((x?.length ?? 0) > 0);
	const x1 = x?.find((v) => v.ind === "wo");
	deepStrictEqual(x1, { key: "w", ind: "wo", preeditShow: "w" });
});

test("拼音 部分2", () => {
	const x = keys_to_pinyin("nihao", { shuangpin: "自然码" }).at(-1);
	ok((x?.length ?? 0) > 0);
	const x1 = x?.find((v) => v.ind === "ou");
	deepStrictEqual(x1, { key: "o", ind: "ou", preeditShow: "o" });
});

test("拼音 部分3", () => {
	const x = keys_to_pinyin("a");
	deepStrictEqual(x, [
		[
			{ key: "a", ind: "a", preeditShow: "a" },
			{ key: "a", ind: "ang", preeditShow: "a" },
			{ key: "a", ind: "ai", preeditShow: "a" },
			{ key: "a", ind: "an", preeditShow: "a" },
			{ key: "a", ind: "ao", preeditShow: "a" },
			{ key: "a", ind: "a", preeditShow: "a" }, // todo 去重
		],
	]);
});

test("拼音 部分4", () => {
	const x = keys_to_pinyin("a").at(-1);
	ok((x?.length ?? 0) > 0);
	deepStrictEqual(
		x?.find((v) => v.ind === "a"),
		{ key: "a", ind: "a", preeditShow: "a" },
	);
	deepStrictEqual(
		x?.find((v) => v.ind === "ai"),
		{ key: "a", ind: "ai", preeditShow: "a" },
	);
	const x1 = keys_to_pinyin("tma", { shuangpin: "自然码" }).at(-1);
	ok((x1?.length ?? 0) > 0);
	deepStrictEqual(
		x1?.find((v) => v.ind === "a"),
		{ key: "a", ind: "a", preeditShow: "a" },
	);
	deepStrictEqual(
		x1?.find((v) => v.ind === "ai"),
		{ key: "a", ind: "ai", preeditShow: "a" },
	);
	const x2 = keys_to_pinyin("tmaa", { shuangpin: "自然码" }).at(-1);
	ok((x2?.length ?? 0) > 0);
	console.log(x2);

	deepStrictEqual(x2?.at(-1), { key: "aa", ind: "a", preeditShow: "a" });
});

test("拼音 分隔符", () => {
	const x = keys_to_pinyin("ni'");
	deepStrictEqual(x, [[{ key: "ni'", ind: "ni", preeditShow: "ni" }]]);
	const x1 = keys_to_pinyin("ni'hao'wo");
	deepStrictEqual(x1, [
		[{ key: "ni'", ind: "ni", preeditShow: "ni" }],
		[{ key: "hao'", ind: "hao", preeditShow: "hao" }],
		[{ key: "wo", ind: "wo", preeditShow: "wo" }],
	]);
});

test("双拼", () => {
	const x = keys_to_pinyin("xxxx", { shuangpin: "自然码" });
	deepStrictEqual(x, [
		[{ key: "xx", ind: "xie", preeditShow: "xie" }],
		[{ key: "xx", ind: "xie", preeditShow: "xie" }],
	]);
});

test("字转化拼音", () => {
	const pinyin = load_pinyin();
	const x = pinyin.trans("你好");
	deepStrictEqual(x, [["ni"], ["hao"]]);
});
