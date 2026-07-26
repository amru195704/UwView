// IME（日本語入力）変換中のキーイベントを Avalonia に渡さないためのガード。
//
// Avalonia 12.0.5 の browser バックエンド（avalonia.js の subscribeKeyEvents）は
// keydown を isComposing で判定せずそのまま OnKeyDown へ流す。Chrome では変換中の
// keydown も code に実キー（KeyT 等）が入るため、Avalonia が文字として解決してしまい
// 「東京」を入力すると "toukyou東京" のようにローマ字が残る。
//
// composition 中の keydown/keyup だけを capture フェーズで止める。
// preventDefault はしないので IME 自体（hidden input への入力）は通常どおり動作し、
// 確定文字列は Avalonia の compositionstart/update/end 経路で正しく届く。
(() => {
    let composing = false;

    const doc = document;
    doc.addEventListener("compositionstart", () => { composing = true; }, true);
    // 確定直後に composing を落とす。compositionend は確定 keydown より後に来るため、
    // 確定用の Enter/Space は下の判定で composing=true として抑制される。
    doc.addEventListener("compositionend", () => { composing = false; }, true);

    const block = (e) => {
        if (composing || e.isComposing || e.key === "Process" || e.keyCode === 229)
            e.stopImmediatePropagation();
    };
    doc.addEventListener("keydown", block, true);
    doc.addEventListener("keyup", block, true);
})();
