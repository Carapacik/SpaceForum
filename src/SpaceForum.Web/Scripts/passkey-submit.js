const browserSupportsPasskeys =
    typeof navigator.credentials !== 'undefined' &&
    typeof window.PublicKeyCredential !== 'undefined' &&
    typeof window.PublicKeyCredential.parseCreationOptionsFromJSON === 'function' &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === 'function';

async function fetchWithErrorHandling(url, options = {}) {
    const response = await fetch(url, { credentials: 'include', ...options });
    if (!response.ok) {
        throw new Error(`The server responded with status ${response.status}.`);
    }
    return response;
}

async function createCredential(headers, signal) {
    const response = await fetchWithErrorHandling('/account/passkeycreationoptions', { method: 'POST', headers, signal });
    return navigator.credentials.create({
        publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(await response.json()),
        signal
    });
}

async function requestCredential(login, mediation, headers, signal) {
    const value = encodeURIComponent(login ?? '');
    const response = await fetchWithErrorHandling(`/account/passkeyrequestoptions?username=${value}`, { method: 'POST', headers, signal });
    return navigator.credentials.get({
        publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(await response.json()),
        mediation,
        signal
    });
}

customElements.define('passkey-submit', class extends HTMLElement {
    static formAssociated = true;

    connectedCallback() {
        this.internals = this.attachInternals();
        this.attrs = {
            operation: this.getAttribute('operation'),
            name: this.getAttribute('name'),
            emailName: this.getAttribute('email-name'),
            requestTokenName: this.getAttribute('request-token-name'),
            requestTokenValue: this.getAttribute('request-token-value')
        };
        this.internals.form.addEventListener('submit', (event) => {
            if (event.submitter?.name === '__passkeySubmit') {
                event.preventDefault();
                this.obtainAndSubmitCredential();
            }
        });
        this.tryAutofillPasskey();
    }

    disconnectedCallback() {
        this.abortController?.abort();
    }

    async obtainCredential(useConditionalMediation, signal) {
        if (!browserSupportsPasskeys) {
            throw new Error('Some passkey features are missing. Please update your browser.');
        }
        const headers = { [this.attrs.requestTokenName]: this.attrs.requestTokenValue };
        if (this.attrs.operation === 'Create') {
            return createCredential(headers, signal);
        }
        if (this.attrs.operation === 'Request') {
            const login = new FormData(this.internals.form).get(this.attrs.emailName);
            return requestCredential(login, useConditionalMediation ? 'conditional' : undefined, headers, signal);
        }
        throw new Error(`Unknown passkey operation '${this.attrs.operation}'.`);
    }

    async obtainAndSubmitCredential(useConditionalMediation = false) {
        this.abortController?.abort();
        this.abortController = new AbortController();
        const formData = new FormData();
        try {
            const credential = await this.obtainCredential(useConditionalMediation, this.abortController.signal);
            formData.append(`${this.attrs.name}.CredentialJson`, JSON.stringify(credential));
        } catch (error) {
            if (error.name === 'AbortError') return;
            if (useConditionalMediation) return;
            formData.append(`${this.attrs.name}.Error`, error.name === 'NotAllowedError'
                ? 'No passkey was provided by the authenticator.'
                : error.message);
        }
        this.internals.setFormValue(formData);
        this.internals.form.submit();
    }

    async tryAutofillPasskey() {
        if (browserSupportsPasskeys && this.attrs.operation === 'Request' && await PublicKeyCredential.isConditionalMediationAvailable?.()) {
            await this.obtainAndSubmitCredential(true);
        }
    }
});
