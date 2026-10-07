// nginx syntax highlighting for Monaco. Monaco has no built-in nginx language, so it is registered here.
// Called from Blazor once the editor is initialised (Monaco is loaded asynchronously, so it cannot run at script load).
window.roastgateRegisterNginx = function () {
    if (window.__roastgateNginxRegistered || typeof monaco === 'undefined') {
        return;
    }
    window.__roastgateNginxRegistered = true;

    monaco.languages.register({ id: 'nginx' });

    monaco.languages.setLanguageConfiguration('nginx', {
        comments: { lineComment: '#' },
        brackets: [['{', '}']],
        autoClosingPairs: [
            { open: '{', close: '}' },
            { open: '"', close: '"', notIn: ['string', 'comment'] },
            { open: "'", close: "'", notIn: ['string', 'comment'] }
        ],
        surroundingPairs: [
            { open: '{', close: '}' },
            { open: '"', close: '"' },
            { open: "'", close: "'" }
        ]
    });

    monaco.languages.setMonarchTokensProvider('nginx', {
        blocks: ['http', 'server', 'location', 'upstream', 'events', 'map', 'geo', 'types', 'if', 'limit_except', 'stream', 'mail'],
        tokenizer: {
            root: [
                [/#.*$/, 'comment'],
                [/"([^"\\]|\\.)*"/, 'string'],
                [/'([^'\\]|\\.)*'/, 'string'],
                [/\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*/, 'variable'],
                // The first word of a statement is the directive name.
                [/^(\s*)([A-Za-z_][A-Za-z0-9_]*)/, ['white', { cases: { '@blocks': 'keyword.block', '@default': 'keyword' } }]],
                [/(;|\{)(\s*)([A-Za-z_][A-Za-z0-9_]*)/, ['delimiter', 'white', { cases: { '@blocks': 'keyword.block', '@default': 'keyword' } }]],
                [/\b\d+(\.\d+)?[kKmMgGsdhw]?\b/, 'number'],
                [/[{}]/, 'delimiter.bracket'],
                [/;/, 'delimiter'],
                [/[~*=^]+/, 'operator'],
                [/[^\s;{}#"'$]+/, 'identifier']
            ]
        }
    });

    monaco.editor.defineTheme('roastgate', {
        base: 'vs',
        inherit: true,
        rules: [
            { token: 'keyword', foreground: '9f560d', fontStyle: 'bold' },
            { token: 'keyword.block', foreground: '1c1e21', fontStyle: 'bold' },
            { token: 'comment', foreground: '6b7385', fontStyle: 'italic' },
            { token: 'variable', foreground: '0b6b8a' },
            { token: 'string', foreground: '2f6f3e' },
            { token: 'number', foreground: 'a8321d' },
            { token: 'operator', foreground: '525860' }
        ],
        colors: {
            'editor.background': '#ffffff',
            'editorLineNumber.foreground': '#8d949e',
            'editor.lineHighlightBackground': '#f5f6f7'
        }
    });
};

// Monaco is loaded asynchronously (AMD). Creating an editor before it is ready makes BlazorMonaco throw, and an unhandled
// exception takes the whole Blazor circuit down. Blazor awaits this promise before it renders the editor component.
window.roastgateMonacoReady = function () {
    return new Promise(function (resolve) {
        if (typeof monaco !== 'undefined' && monaco.editor) {
            resolve(true);
            return;
        }
        require(['vs/editor/editor.main'], function () {
            resolve(true);
        });
    });
};
