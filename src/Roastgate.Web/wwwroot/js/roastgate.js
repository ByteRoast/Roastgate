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
        const response = await fetch('/client-address', {credentials: 'same-origin'});
        
        return response.ok ? (await response.text()).trim() : '';
    } catch (error) {
        return '';
    }
};

// ---- Passkeys (WebAuthn) ---------------------------------------------------------------
// The server speaks the JSON form of the WebAuthn options (binary values are base64url strings). The conversions are done here by
// hand rather than with PublicKeyCredential.parse...FromJSON, which older browsers do not have.

(function () {
    function decode(value) {
        const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
        const text = atob(base64 + '='.repeat((4 - (base64.length % 4)) % 4));
        const bytes = new Uint8Array(text.length);

        for (let index = 0; index < text.length; index++) {
            bytes[index] = text.charCodeAt(index);
        }
        
        return bytes.buffer;
    }

    function encode(buffer) {
        let text = '';
        
        for (const byte of new Uint8Array(buffer)) {
            text += String.fromCharCode(byte);
        }
        
        return btoa(text).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    function withDescriptors(list) {
        return (list || []).map(function (item) {
            return Object.assign({}, item, {id: decode(item.id)});
        });
    }

    function creationOptions(options) {
        return Object.assign({}, options, {
            challenge: decode(options.challenge),
            user: Object.assign({}, options.user, {id: decode(options.user.id)}),
            excludeCredentials: withDescriptors(options.excludeCredentials),
        });
    }

    function requestOptions(options) {
        return Object.assign({}, options, {
            challenge: decode(options.challenge),
            allowCredentials: withDescriptors(options.allowCredentials),
        });
    }

    function describeError(error) {
        if (error && error.name === 'NotAllowedError') {
            return 'The request was cancelled or timed out.';
        }
        
        if (error && error.name === 'InvalidStateError') {
            return 'This passkey is already registered.';
        }
        
        return 'The passkey could not be used: ' + ((error && error.message) || 'unknown error') + '.';
    }

    function supported() {
        return !!(window.PublicKeyCredential && navigator.credentials);
    }

    // A passkey is bound to a host name: browsers refuse an IP address ("The operation is insecure").
    function usesIpAddress() {
        return /^\d{1,3}(\.\d{1,3}){3}$/.test(location.hostname) || location.hostname.indexOf(':') >= 0;
    }

    const ipAddressMessage = 'Passkeys do not work on an IP address. Open Roastgate through a host name instead, such as http://localhost:' +
        (location.port || '8181') + ' or your domain.';

    window.roastgatePasskeysSupported = supported;

    // Asks the authenticator to create a passkey. Returns { response } (the JSON the server verifies) or { error }.
    window.roastgatePasskeyCreate = async function (optionsJson) {
        if (usesIpAddress()) {
            return {error: ipAddressMessage};
        }
        
        if (!supported()) {
            return {error: 'This browser does not support passkeys here. They need HTTPS (or localhost).'};
        }
        
        try {
            const credential = await navigator.credentials.create({publicKey: creationOptions(JSON.parse(optionsJson))});
            
            return {
                response: JSON.stringify({
                    id: credential.id,
                    rawId: encode(credential.rawId),
                    type: credential.type,
                    clientExtensionResults: credential.getClientExtensionResults(),
                    response: {
                        attestationObject: encode(credential.response.attestationObject),
                        clientDataJSON: encode(credential.response.clientDataJSON),
                        transports: credential.response.getTransports ? credential.response.getTransports() : [],
                    },
                }),
            };
        } catch (error) {
            return {error: describeError(error)};
        }
    };

    function antiforgeryToken() {
        const field = document.querySelector('input[name="__RequestVerificationToken"]');
        
        return field ? field.value : '';
    }

    async function post(url, body) {
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {'Content-Type': 'application/json', 'RequestVerificationToken': antiforgeryToken()},
            body: JSON.stringify(body),
        });
        
        let data = {};
        
        try {
            data = await response.json();
        } catch (error) {
            // An empty or non-JSON answer: the status says enough.
        }
        
        return {ok: response.ok, data: data};
    }

    // The sign-in page: every element with data-passkey-signin starts the ceremony. data-passkey-ticket names the input that holds
    // the ticket of the second step, and data-passkey-error the element that shows a problem.
    async function signIn(button) {
        const errorElement = document.getElementById(button.dataset.passkeyError);
        const ticketInput = button.dataset.passkeyTicket ? document.getElementById(button.dataset.passkeyTicket) : null;
        const ticket = ticketInput ? ticketInput.value : '';
        const returnUrl = new URLSearchParams(location.search).get('returnUrl') || '';

        function fail(message, restart) {
            if (errorElement) {
                errorElement.textContent = message;
                errorElement.hidden = false;
            }
            
            button.disabled = false;
            
            if (restart) {
                setTimeout(function () {
                    location.href = '/login';
                }, 2500);
            }
        }

        if (errorElement) {
            errorElement.hidden = true;
        }
        
        if (usesIpAddress()) {
            fail(ipAddressMessage);
        
            return;
        }
        
        if (!supported()) {
            fail('This browser does not support passkeys here. They need HTTPS (or localhost).');
        
            return;
        }

        button.disabled = true;
        
        try {
            const started = await post('/passkeys/sign-in/options', {ticket: ticket});
        
            if (!started.ok) {
                fail(started.data.error || 'Passkey sign-in is not available right now.');
        
                return;
            }

            const credential = await navigator.credentials.get({publicKey: requestOptions(started.data.options)});
            const finished = await post('/passkeys/sign-in/verify', {
                ceremonyId: started.data.ceremonyId,
                ticket: ticket,
                returnUrl: returnUrl,
                response: {
                    id: credential.id,
                    rawId: encode(credential.rawId),
                    type: credential.type,
                    clientExtensionResults: credential.getClientExtensionResults(),
                    response: {
                        authenticatorData: encode(credential.response.authenticatorData),
                        clientDataJSON: encode(credential.response.clientDataJSON),
                        signature: encode(credential.response.signature),
                        userHandle: credential.response.userHandle ? encode(credential.response.userHandle) : null,
                    },
                },
            });

            if (finished.ok) {
                location.href = finished.data.redirect || '/';
        
                return;
            }
        
            fail(finished.data.error || 'That passkey was not accepted.', finished.data.restart);
        } catch (error) {
            fail(describeError(error));
        }
    }

    document.addEventListener('click', function (event) {
        const button = event.target.closest ? event.target.closest('[data-passkey-signin]') : null;
        
        if (button) {
            event.preventDefault();
            signIn(button);
        }
    });
})();
