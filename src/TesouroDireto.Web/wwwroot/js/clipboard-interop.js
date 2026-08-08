window.clipboardInterop = {
    copy: function (text) {
        return navigator.clipboard.writeText(text);
    }
};
