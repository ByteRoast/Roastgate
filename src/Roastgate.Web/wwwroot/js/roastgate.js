// Copies text to the clipboard. navigator.clipboard only exists in secure contexts (HTTPS or localhost), so it can be missing
// when Roastgate is reached over plain HTTP: the caller falls back to showing the text selected.
window.roastgateCopy = async function (text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (error) {
        return false;
    }
};

// The address this browser connects from, as Roastgate sees it (the server answers with the connection's remote address).
window.roastgateClientAddress = async function () {
    try {
        const response = await fetch('/client-address', { credentials: 'same-origin' });
        return response.ok ? (await response.text()).trim() : '';
    } catch (error) {
        return '';
    }
};
