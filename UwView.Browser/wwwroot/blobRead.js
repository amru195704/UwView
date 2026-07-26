// UwView Browser head: File(Blob) をJS側に保持し、blob.slice でランダム読みを提供する。
// ファイル全体はメモリに載せない（slice した範囲だけ ArrayBuffer 化）。
// 注意: .NET の JSImport は Promise<byte[]> を扱えないため、
//       readSliceBegin(Promise<token>) → readSliceTake(byte[] 同期) の2段方式にしている。

const files = new Map();
let nextFileId = 1;

const results = new Map();
let nextToken = 1;

// ファイル選択ダイアログを開き、選択結果を1行1ファイルのタブ区切りで返す。
// 形式: "id\tsize\tname\n..."（キャンセル時は ""）
export function pickFiles() {
    return new Promise((resolve) => {
        const input = document.createElement("input");
        input.type = "file";
        input.multiple = true;
        input.onchange = () => {
            let lines = "";
            for (const f of input.files) {
                const id = nextFileId++;
                files.set(id, f);
                lines += id + "\t" + f.size + "\t" + f.name + "\n";
            }
            resolve(lines);
        };
        input.oncancel = () => resolve("");
        input.click();
    });
}

// blob.slice を読み、結果を保管してトークンを返す（範囲外は短くなる）。
export async function readSliceBegin(id, offset, length) {
    const f = files.get(id);
    let data;
    if (!f) {
        data = new Uint8Array(0);
    } else {
        const end = Math.min(offset + length, f.size);
        data = offset >= end
            ? new Uint8Array(0)
            : new Uint8Array(await f.slice(offset, end).arrayBuffer());
    }
    const token = nextToken++;
    results.set(token, data);
    return token;
}

// readSliceBegin の結果を .NET 側バッファ（MemoryView）へ直接書き込む（同期・1回限り）。
// 書き込めたバイト数を返す。byte[] を JSType.Array<Number> で返すとバイト単位で
// double へ変換されて極端に遅いため、MemoryView への一括コピーにしている。
export function readSliceTake(token, buffer) {
    const d = results.get(token);
    results.delete(token);
    if (!d) return 0;
    const n = Math.min(d.length, buffer.length);
    buffer.set(n === d.length ? d : d.subarray(0, n));
    return n;
}

// タブを閉じたら File 参照を解放する。
export function closeFile(id) {
    files.delete(id);
}
