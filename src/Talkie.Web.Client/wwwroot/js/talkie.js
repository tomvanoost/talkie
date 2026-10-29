// Small JS helpers used by the Talkie Blazor client.
window.talkie = {
    storage: {
        get: key => { try { return localStorage.getItem(key); } catch { return null; } },
        set: (key, value) => { try { localStorage.setItem(key, value); } catch { } },
        remove: key => { try { localStorage.removeItem(key); } catch { } },
    },

    scrollToBottom: (element, smooth) => {
        if (!element) return;
        element.scrollTo({ top: element.scrollHeight, behavior: smooth ? 'smooth' : 'auto' });
    },

    isNearBottom: element => !element || element.scrollHeight - element.scrollTop - element.clientHeight < 120,

    focus: element => { if (element) element.focus(); },

    // Wires a <textarea> composer: Enter sends (Shift+Enter = new line) and pasted images are uploaded.
    registerComposer: (element, dotnet) => {
        if (!element || element._talkie) return;
        element._talkie = true;

        element.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                dotnet.invokeMethodAsync('SendFromKeyboard', element.value);
            }
        });

        element.addEventListener('paste', async e => {
            const items = Array.from(e.clipboardData?.items || []);
            const files = items.filter(i => i.kind === 'file').map(i => i.getAsFile()).filter(f => f);
            if (files.length === 0) return;
            e.preventDefault();
            for (const file of files) {
                const buffer = await file.arrayBuffer();
                let binary = '';
                const bytes = new Uint8Array(buffer);
                for (let i = 0; i < bytes.length; i += 0x8000) {
                    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
                }
                const name = file.name && file.name !== 'image.png' ? file.name : `pasted-${Date.now()}.${(file.type.split('/')[1] || 'png')}`;
                await dotnet.invokeMethodAsync('OnPastedFile', name, file.type || 'application/octet-stream', btoa(binary));
            }
        });
    },

    clear: element => { if (element) { element.value = ''; element.style.height = ''; } },

    autoGrow: element => {
        if (!element || element._grow) return;
        element._grow = true;
        element.addEventListener('input', () => {
            element.style.height = 'auto';
            element.style.height = Math.min(element.scrollHeight, 180) + 'px';
        });
    },
};
