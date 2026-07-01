import type { Result, UserData } from "../../main.ts";
import type { inputLog } from "../../server.ts";

export class lime {
	private getHeader() {
		return new Headers({
			"Content-Type": "application/json",
		});
	}
	private getServerUrl(): string {
		const baseUrl = new URL(
			new URLSearchParams(location.search).get("server") || location.origin,
		);
		baseUrl.pathname = "/api";
		return baseUrl.toString();
	}
	async candidates(keys: string) {
		const data = fetch(`${this.getServerUrl()}/candidates`, {
			method: "POST",
			headers: this.getHeader(),
			body: JSON.stringify({ keys: keys }),
		});
		const res = await (await data).json();
		return res as Result;
	}
	async commit(word: string, newT: boolean) {
		await fetch(`${this.getServerUrl()}/commit`, {
			method: "POST",
			headers: this.getHeader(),
			body: JSON.stringify({ text: word, new: newT }),
		});
	}
	async userData() {
		const data = await fetch(`${this.getServerUrl()}/userdata`, {
			method: "GET",
			headers: this.getHeader(),
		});
		const res = await data.json();
		return res as UserData;
	}
	async inputlog() {
		const data = await fetch(`${this.getServerUrl()}/inputlog`, {
			method: "GET",
			headers: this.getHeader(),
		});
		const res = await data.json();
		return res as typeof inputLog;
	}
	async pushText(text: string) {
		await fetch(`${this.getServerUrl()}/learntext`, {
			method: "POST",
			headers: this.getHeader(),
			body: text,
		});
	}
}
