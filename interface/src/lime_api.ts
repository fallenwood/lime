import type { Result, UserData } from "../../main.ts";
import type { inputLog } from "../../server.ts";

function unavailable(): never {
	throw new Error("服务器已移除 HTTP 接口；浏览器前端需要单独的命名管道桥接层");
}

export class lime {
	async candidates(_keys: string): Promise<Result> {
		unavailable();
	}
	async commit(_word: string, _newT: boolean): Promise<void> {
		unavailable();
	}
	async userData(): Promise<UserData> {
		unavailable();
	}
	async inputlog(): Promise<typeof inputLog> {
		unavailable();
	}
	async pushText(_text: string): Promise<void> {
		unavailable();
	}
}
