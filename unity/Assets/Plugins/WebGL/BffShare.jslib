// ブラウザ版(WebGL)で、画像をシェアする。Web版(index.html)と同じ動き:
//   端末の共有画面が使えれば、画像つきで渡す(戻り値 1)。
//   使えなければ(主にPC)、画像を保存する(戻り値 0)。呼び出し側が、Xの投稿画面を開く。
mergeInto(LibraryManager.library, {
  BffShare: function (dataPtr, size, namePtr, textPtr, urlPtr) {
    var name = UTF8ToString(namePtr);
    var text = UTF8ToString(textPtr);
    var url = UTF8ToString(urlPtr);
    var bytes = new Uint8Array(HEAPU8.buffer, dataPtr, size).slice();
    var blob = new Blob([bytes], { type: 'image/png' });
    try {
      var file = new File([blob], name, { type: 'image/png' });
      if (navigator.canShare && navigator.canShare({ files: [file] })) {
        navigator.share({ files: [file], text: text, url: url }).catch(function () {});
        return 1;
      }
    } catch (e) {}
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 5000);
    return 0;
  }
});
