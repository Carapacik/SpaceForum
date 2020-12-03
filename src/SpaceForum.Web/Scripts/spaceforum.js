let pendingTopicPosition;
let topicPositionCaptureReady = false;
const topicPositionStorageKey = 'spaceforum:topic-position';

function formatLocalTimes(root = document) {
    const locale = document.documentElement.lang || 'en';
    const exactFormatter = new Intl.DateTimeFormat(locale, {
        dateStyle: 'medium',
        timeStyle: 'short'
    });
    const dateFormatter = new Intl.DateTimeFormat(locale, {
        dateStyle: 'medium'
    });
    const monthYearFormatter = new Intl.DateTimeFormat(locale, {
        month: 'long',
        year: 'numeric'
    });

    root.querySelectorAll('time[data-local-time]').forEach((element) => {
        const instant = new Date(element.dateTime);
        if (Number.isNaN(instant.valueOf())) {
            return;
        }

        element.textContent = element.dataset.localTime === 'date-time'
            ? exactFormatter.format(instant)
            : element.dataset.localTime === 'month-year'
                ? monthYearFormatter.format(instant)
                : dateFormatter.format(instant);
        element.title = exactFormatter.format(instant);
    });
}

function initializePreservedTopicPositions(root = document) {
    if (!topicPositionCaptureReady) {
        document.addEventListener('submit', (event) => {
            const form = event.target.closest('[data-preserve-topic-position]');
            if (!form || (form.dataset.confirmSubmit && form.dataset.confirmed !== 'true')) {
                return;
            }

            const selectedPost = document.querySelector('[data-post-stream] [data-post-number][aria-current="true"]');
            const requestedHash = form.dataset.preserveTopicPosition;
            pendingTopicPosition = {
                path: window.location.pathname,
                hash: requestedHash?.startsWith('#')
                    ? requestedHash
                    : window.location.hash || (selectedPost?.id ? `#${selectedPost.id}` : ''),
                scrollY: window.scrollY
            };
            window.sessionStorage.setItem(topicPositionStorageKey, JSON.stringify(pendingTopicPosition));
        }, true);
        topicPositionCaptureReady = true;
    }

    if (!pendingTopicPosition) {
        try {
            const stored = window.sessionStorage.getItem(topicPositionStorageKey);
            if (stored) {
                pendingTopicPosition = JSON.parse(stored);
            }
        } catch {
            window.sessionStorage.removeItem(topicPositionStorageKey);
        }
    }

    if (!pendingTopicPosition) {
        return;
    }

    const position = pendingTopicPosition;
    if (position.path !== window.location.pathname) {
        pendingTopicPosition = undefined;
        window.sessionStorage.removeItem(topicPositionStorageKey);
        return;
    }

    pendingTopicPosition = undefined;
    window.sessionStorage.removeItem(topicPositionStorageKey);
    const permalink = new URL(window.location.href);
    permalink.hash = position.hash;
    window.history.replaceState(null, '', permalink.href);

    document.querySelectorAll('[data-post-stream] [data-post-number]').forEach((post) => {
        const isSelected = position.hash && `#${post.id}` === position.hash;
        post.classList.toggle('is-selected', Boolean(isSelected));
        if (isSelected) {
            post.setAttribute('aria-current', 'true');
        } else {
            post.removeAttribute('aria-current');
        }
    });

    window.requestAnimationFrame(() => {
        window.requestAnimationFrame(() => window.scrollTo({ top: position.scrollY, behavior: 'auto' }));
    });
}

function initializeRequiredForms(root = document) {
    root.querySelectorAll('.sf-required-form').forEach((form) => {
        if (form.dataset.validationReady === 'true') {
            return;
        }

        const submit = form.querySelector('.sf-submit');
        if (!submit) {
            return;
        }

        const refresh = () => {
            submit.disabled = !form.checkValidity();
            submit.setAttribute('aria-disabled', String(submit.disabled));
        };
        form.addEventListener('input', refresh);
        form.addEventListener('change', refresh);
        form.dataset.validationReady = 'true';
        refresh();
    });
}

function initializePostNavigators(root = document) {
    root.querySelectorAll('[data-post-navigator]').forEach((navigator) => {
        if (navigator.dataset.navigatorReady === 'true') {
            return;
        }

        const stream = navigator.closest('[data-post-stream]');
        const slider = navigator.querySelector('[data-post-slider]');
        const output = navigator.querySelector('[data-current-post]');
        const posts = [...stream.querySelectorAll('[data-post-number]')];
        if (!slider || !output || posts.length === 0) {
            return;
        }

        let dragging = false;
        let scrollFrame;
        const scrollToSelectedPost = () => {
            const post = posts[Number(slider.value)];
            if (!post) {
                return;
            }

            output.textContent = post.dataset.postNumber;
            window.cancelAnimationFrame(scrollFrame);
            scrollFrame = window.requestAnimationFrame(() => {
                const top = window.scrollY + post.getBoundingClientRect().top - 96;
                window.scrollTo({ top: Math.max(0, top), behavior: 'auto' });
            });
        };

        slider.addEventListener('pointerdown', () => {
            dragging = true;
        });
        slider.addEventListener('input', scrollToSelectedPost);
        slider.addEventListener('change', scrollToSelectedPost);
        const finishDragging = () => {
            dragging = false;
            scrollToSelectedPost();
        };
        slider.addEventListener('pointerup', finishDragging);
        slider.addEventListener('pointercancel', finishDragging);

        const observer = new IntersectionObserver((entries) => {
            if (dragging) {
                return;
            }

            const visible = entries
                .filter((entry) => entry.isIntersecting)
                .sort((left, right) => left.boundingClientRect.top - right.boundingClientRect.top)[0];
            if (!visible) {
                return;
            }

            const index = posts.indexOf(visible.target);
            slider.value = String(index);
            output.textContent = visible.target.dataset.postNumber;
        }, { rootMargin: '-15% 0px -65% 0px', threshold: 0 });

        posts.forEach((post) => observer.observe(post));
        navigator.dataset.navigatorReady = 'true';
    });
}

function initializePostPermalinks(root = document) {
    const streams = root.querySelectorAll('[data-post-stream]');
    streams.forEach((stream) => {
        if (stream.dataset.permalinksReady === 'true') {
            return;
        }

        const posts = [...stream.querySelectorAll('[data-post-number]')];
        const requestedPost = /^#post-(\d+)$/.exec(window.location.hash);
        if (requestedPost && !stream.querySelector(`#post-${requestedPost[1]}`)) {
            const pageSize = Number(stream.dataset.pageSize);
            const currentPage = Number(stream.dataset.currentPage);
            const targetPage = Math.max(1, Math.ceil(Number(requestedPost[1]) / pageSize));
            if (Number.isFinite(pageSize) && pageSize > 0 && targetPage !== currentPage) {
                const target = new URL(window.location.href);
                if (targetPage === 1) {
                    target.searchParams.delete('page');
                } else {
                    target.searchParams.set('page', String(targetPage));
                }
                window.location.replace(target.href);
                return;
            }
        }
        const selectPost = (hash, scroll = true) => {
            const match = /^#post-(\d+)$/.exec(hash);
            const selected = match ? stream.querySelector(`#post-${match[1]}`) : null;
            posts.forEach((post) => {
                const isSelected = post === selected;
                post.classList.toggle('is-selected', isSelected);
                if (isSelected) {
                    post.setAttribute('aria-current', 'true');
                } else {
                    post.removeAttribute('aria-current');
                }
            });

            if (selected && scroll) {
                window.requestAnimationFrame(() => selected.scrollIntoView({ behavior: 'auto', block: 'start' }));
            }
            return selected;
        };

        const copyText = async (text) => {
            try {
                if (navigator.clipboard?.writeText && window.isSecureContext) {
                    await navigator.clipboard.writeText(text);
                    return true;
                }
            } catch {
                // Fall through for browsers that expose the API but deny it.
            }

            const input = document.createElement('textarea');
            input.value = text;
            input.setAttribute('readonly', '');
            input.style.position = 'fixed';
            input.style.opacity = '0';
            document.body.append(input);
            input.select();
            const copied = document.execCommand('copy');
            input.remove();
            return copied;
        };

        const setSelectedPost = (postId) => {
            const hash = `#${postId}`;
            if (window.location.hash === hash) {
                selectPost(hash);
            } else {
                const permalink = new URL(window.location.href);
                permalink.hash = hash;
                window.history.pushState(null, '', permalink.href);
                selectPost(hash);
            }
        };

        stream.querySelectorAll('[data-post-permalink]').forEach((link) => {
            link.addEventListener('click', (event) => {
                event.preventDefault();
                setSelectedPost(link.hash.slice(1));
            });
        });

        stream.querySelectorAll('[data-copy-post-link]').forEach((button) => {
            button.addEventListener('click', async () => {
                setSelectedPost(button.dataset.copyPostLink);
                if (await copyText(window.location.href)) {
                    button.classList.add('is-copied');
                    window.setTimeout(() => button.classList.remove('is-copied'), 1200);
                }
            });
        });

        window.addEventListener('hashchange', () => selectPost(window.location.hash));
        window.addEventListener('popstate', () => selectPost(window.location.hash));
        stream.dataset.permalinksReady = 'true';
        selectPost(window.location.hash);
    });
}

function initializeSubmittedPost(root = document) {
    const marker = root.querySelector('[data-select-post-after-submit]');
    if (!marker || marker.dataset.selectionComplete === 'true') {
        return;
    }

    const postId = marker.dataset.selectPostAfterSubmit;
    const selected = document.getElementById(postId);
    if (!selected) {
        return;
    }

    if (marker.dataset.clearDraftKey) {
        try {
            window.localStorage.removeItem(`spaceforum:draft:${marker.dataset.clearDraftKey}`);
        } catch {
            // Local storage can be unavailable in privacy-restricted browsers.
        }
    }

    document.querySelectorAll('.sf-post').forEach((post) => {
        const isSelected = post === selected;
        post.classList.toggle('is-selected', isSelected);
        if (isSelected) {
            post.setAttribute('aria-current', 'true');
        } else {
            post.removeAttribute('aria-current');
        }
    });

    const permalink = new URL(window.location.href);
    permalink.hash = postId;
    window.history.replaceState(null, '', permalink.href);
    marker.dataset.selectionComplete = 'true';
    window.requestAnimationFrame(() => selected.scrollIntoView({ behavior: 'auto', block: 'start' }));
}

function initializeMarkdownComposers(root = document) {
    root.querySelectorAll('[data-markdown-composer]').forEach((composer) => {
        if (composer.dataset.composerReady === 'true') {
            return;
        }

        const textarea = composer.querySelector('textarea');
        const writePanel = composer.querySelector('[data-composer-write]');
        const previewPanel = composer.querySelector('[data-composer-preview]');
        const modeButtons = [...composer.querySelectorAll('[data-composer-mode]')];
        if (!textarea || !writePanel || !previewPanel || modeButtons.length === 0) {
            return;
        }

        let previewRequest = 0;
        let previewTimer;
        let draftTimer;
        const draftStorageKey = composer.dataset.draftKey
            ? `spaceforum:draft:${composer.dataset.draftKey}`
            : undefined;
        const attachmentInput = composer.querySelector('[data-attachment-upload]');
        const attachmentStatus = composer.querySelector('[data-attachment-status]');

        const showPreviewMessage = (message) => {
            const paragraph = document.createElement('p');
            paragraph.className = 'text-content-muted';
            paragraph.textContent = message;
            previewPanel.replaceChildren(paragraph);
        };

        const renderPreview = async () => {
            const request = ++previewRequest;
            const body = textarea.value;
            if (!body.trim()) {
                showPreviewMessage(composer.dataset.previewEmpty || 'Nothing to preview yet.');
                return;
            }

            try {
                const response = await fetch('/api/markdown/preview', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Accept: 'text/html' },
                    body: JSON.stringify({ body })
                });
                if (!response.ok || request !== previewRequest) {
                    throw new Error('Markdown preview failed.');
                }

                previewPanel.innerHTML = await response.text();
            } catch {
                if (request === previewRequest) {
                    showPreviewMessage(composer.dataset.previewError || 'Preview is temporarily unavailable.');
                }
            }
        };

        const selectMode = (mode) => {
            const previewing = mode === 'preview';
            writePanel.hidden = previewing;
            previewPanel.hidden = !previewing;
            modeButtons.forEach((button) => {
                button.setAttribute('aria-selected', String(button.dataset.composerMode === mode));
            });
            if (previewing) {
                void renderPreview();
            } else {
                textarea.focus({ preventScroll: true });
            }
        };

        modeButtons.forEach((button) => {
            button.addEventListener('click', () => selectMode(button.dataset.composerMode));
        });

        composer.querySelectorAll('[data-markdown-action]').forEach((button) => {
            button.addEventListener('click', () => {
                const start = textarea.selectionStart;
                const end = textarea.selectionEnd;
                const selected = textarea.value.slice(start, end);
                let replacement;
                switch (button.dataset.markdownAction) {
                    case 'bold':
                        replacement = `**${selected || 'bold text'}**`;
                        break;
                    case 'inline-code':
                        replacement = `\`${selected || 'code'}\``;
                        break;
                    case 'quote':
                        replacement = (selected || 'quoted text')
                            .split('\n')
                            .map((line) => `> ${line}`)
                            .join('\n');
                        break;
                    case 'code-block':
                        replacement = `\`\`\`\n${selected || 'code'}\n\`\`\``;
                        break;
                    default:
                        return;
                }

                textarea.setRangeText(replacement, start, end, 'end');
                textarea.dispatchEvent(new Event('input', { bubbles: true }));
                textarea.focus();
            });
        });

        if (draftStorageKey && !textarea.value) {
            try {
                const savedDraft = window.localStorage.getItem(draftStorageKey);
                if (savedDraft) {
                    textarea.value = savedDraft;
                    textarea.dispatchEvent(new Event('input', { bubbles: true }));
                }
            } catch {
                // The composer remains usable without persistent local storage.
            }
        }

        textarea.addEventListener('input', () => {
            if (draftStorageKey) {
                window.clearTimeout(draftTimer);
                draftTimer = window.setTimeout(() => {
                    try {
                        if (textarea.value) {
                            window.localStorage.setItem(draftStorageKey, textarea.value);
                        } else {
                            window.localStorage.removeItem(draftStorageKey);
                        }
                    } catch {
                        // The in-memory field remains the source of truth.
                    }
                }, 250);
            }

            if (!previewPanel.hidden) {
                window.clearTimeout(previewTimer);
                previewTimer = window.setTimeout(() => void renderPreview(), 180);
            }
        });

        attachmentInput?.addEventListener('change', async () => {
            const file = attachmentInput.files?.[0];
            const token = composer.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!file || !token) {
                return;
            }

            if (attachmentStatus) {
                attachmentStatus.textContent = `${composer.dataset.uploadingLabel || 'Uploading'} ${file.name}...`;
            }
            const data = new FormData();
            data.append('file', file);
            data.append('__RequestVerificationToken', token);
            try {
                const response = await fetch('/actions/attachments/upload', { method: 'POST', body: data });
                if (!response.ok) {
                    throw new Error('Upload failed.');
                }
                const attachment = await response.json();
                const markdown = attachment.isImage
                    ? `![${attachment.name}](${attachment.url})`
                    : `[${attachment.name}](${attachment.url})`;
                textarea.setRangeText(markdown, textarea.selectionStart, textarea.selectionEnd, 'end');
                textarea.dispatchEvent(new Event('input', { bubbles: true }));
                if (attachmentStatus) {
                    attachmentStatus.textContent = attachment.name;
                }
            } catch {
                if (attachmentStatus) {
                    attachmentStatus.textContent = composer.dataset.uploadError || 'The media could not be uploaded.';
                }
            } finally {
                attachmentInput.value = '';
            }
        });

        composer.dataset.composerReady = 'true';
    });
}

function initializePostReplyActions(root = document) {
    root.querySelectorAll('[data-reply-to-post]').forEach((button) => {
        if (button.dataset.replyReady === 'true') {
            return;
        }

        button.addEventListener('click', () => {
            const composer = document.querySelector('[data-markdown-composer]');
            const textarea = composer?.querySelector('textarea');
            const replyToInput = composer?.querySelector('[data-reply-to-post-id]');
            if (!composer || !textarea) {
                return;
            }

            const attribution = button.dataset.replyAttribution || 'wrote';
            const quote = `> [@${button.dataset.replyLogin}](${button.dataset.replyProfile}) [${attribution}](${button.dataset.replyPermalink}):\n> ${button.dataset.replyExcerpt}\n\n`;
            const spacer = textarea.value && !textarea.value.endsWith('\n\n') ? '\n\n' : '';
            textarea.setRangeText(`${spacer}${quote}`, textarea.selectionStart, textarea.selectionEnd, 'end');
            if (replyToInput) {
                replyToInput.value = button.dataset.replyPostId || '';
            }
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
            composer.querySelector('[data-composer-mode="write"]')?.click();
            composer.scrollIntoView({ behavior: 'smooth', block: 'center' });
            textarea.focus({ preventScroll: true });
        });
        button.dataset.replyReady = 'true';
    });
}

function initializeLanguageSwitches(root = document) {
    root.querySelectorAll('[data-language-switch]').forEach((link) => {
        if (link.dataset.languageReady === 'true') {
            return;
        }

        link.addEventListener('click', () => {
            if (!window.location.hash) {
                return;
            }

            const target = new URL(link.href, window.location.origin);
            const returnUrl = target.searchParams.get('returnUrl');
            if (returnUrl && !returnUrl.includes('#')) {
                target.searchParams.set('returnUrl', `${returnUrl}${window.location.hash}`);
                link.href = target.href;
            }
        });
        link.dataset.languageReady = 'true';
    });
}

function initializeLiveTopics(root = document) {
    root.querySelectorAll('[data-live-topic-id]').forEach((stream) => {
        if (stream.dataset.liveReady === 'true') {
            return;
        }

        const banner = stream.querySelector('[data-live-update]');
        if (!banner) {
            return;
        }

        let interval;
        const poll = async () => {
            if (!stream.isConnected) {
                window.clearInterval(interval);
                return;
            }
            if (document.hidden) {
                return;
            }

            try {
                const response = await fetch(`/api/topics/${stream.dataset.liveTopicId}/activity`, { headers: { Accept: 'application/json' } });
                if (!response.ok) {
                    return;
                }

                const activity = await response.json();
                if (activity.lastPostNumber <= Number(stream.dataset.liveLastPost)) {
                    return;
                }

                const message = document.createElement('span');
                message.textContent = stream.dataset.liveMessage;
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'sf-button-primary';
                button.textContent = stream.dataset.liveLabel;
                button.addEventListener('click', () => {
                    const target = new URL(window.location.href);
                    const pageSize = Number(stream.dataset.pageSize);
                    const targetPage = Math.max(1, Math.ceil(activity.lastPostNumber / pageSize));
                    if (targetPage === 1) {
                        target.searchParams.delete('page');
                    } else {
                        target.searchParams.set('page', String(targetPage));
                    }
                    target.hash = `post-${activity.lastPostNumber}`;
                    window.location.assign(target.href);
                });
                banner.replaceChildren(message, button);
                banner.hidden = false;
            } catch {
                // The next polling interval retries without disrupting reading.
            }
        };

        interval = window.setInterval(() => void poll(), 15000);
        stream.dataset.liveReady = 'true';
    });
}

function initializeSearchSuggestions(root = document) {
    root.querySelectorAll('[data-search-suggest]').forEach((form) => {
        if (form.dataset.suggestionsReady === 'true') {
            return;
        }

        const input = form.querySelector('input[type="search"]');
        const results = form.querySelector('[role="listbox"]');
        if (!input || !results) {
            return;
        }

        let requestNumber = 0;
        let timer;
        const close = () => {
            results.hidden = true;
            results.replaceChildren();
            input.setAttribute('aria-expanded', 'false');
        };
        const refresh = () => {
            window.clearTimeout(timer);
            const query = input.value.trim();
            if (query.length < 2) {
                close();
                return;
            }

            const currentRequest = ++requestNumber;
            timer = window.setTimeout(async () => {
                try {
                    const response = await fetch(`/api/search/suggestions?q=${encodeURIComponent(query)}`, {
                        headers: { Accept: 'application/json' }
                    });
                    if (!response.ok || currentRequest !== requestNumber) {
                        return;
                    }

                    const suggestions = await response.json();
                    results.replaceChildren(...suggestions.map((suggestion) => {
                        const link = document.createElement('a');
                        link.className = 'sf-search-result';
                        link.href = suggestion.url;
                        link.setAttribute('role', 'option');

                        const title = document.createElement('span');
                        title.className = 'sf-search-result-title';
                        title.textContent = suggestion.title;
                        const meta = document.createElement('span');
                        meta.className = 'sf-search-result-meta';
                        meta.textContent = `${suggestion.categoryName} · ${suggestion.authorDisplayName}`;
                        link.append(title, meta);
                        return link;
                    }));
                    results.hidden = suggestions.length === 0;
                    input.setAttribute('aria-expanded', String(suggestions.length > 0));
                } catch {
                    close();
                }
            }, 180);
        };

        input.addEventListener('input', refresh);
        input.addEventListener('focus', refresh);
        input.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                close();
            }
        });
        document.addEventListener('click', (event) => {
            if (!form.contains(event.target)) {
                close();
            }
        });
        form.dataset.suggestionsReady = 'true';
    });
}

function initializeConfirmations(root = document) {
    const dialog = document.getElementById('confirmation-dialog');
    if (!(dialog instanceof HTMLDialogElement) || dialog.dataset.confirmationReady === 'true') {
        return;
    }

    const title = dialog.querySelector('[data-confirm-dialog-title]');
    const message = dialog.querySelector('[data-confirm-dialog-message]');
    const accept = dialog.querySelector('[data-confirm-dialog-accept]');
    if (!title || !message || !accept) {
        return;
    }

    let pendingForm;
    let pendingSubmitter;
    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-confirm-submit]');
        if (!form) {
            return;
        }

        if (form.dataset.confirmed === 'true') {
            delete form.dataset.confirmed;
            return;
        }

        event.preventDefault();
        pendingForm = form;
        pendingSubmitter = event.submitter;
        title.textContent = form.dataset.confirmTitle || 'Are you sure?';
        message.textContent = form.dataset.confirmSubmit;
        accept.textContent = form.dataset.confirmLabel || 'Confirm';
        accept.className = form.dataset.confirmTone === 'danger' ? 'btn-danger' : 'sf-button-primary';
        dialog.showModal();
    });
    accept.addEventListener('click', () => {
        if (!pendingForm) {
            return;
        }

        const form = pendingForm;
        const submitter = pendingSubmitter;
        pendingForm = undefined;
        pendingSubmitter = undefined;
        dialog.close();
        form.dataset.confirmed = 'true';
        form.requestSubmit(submitter);
    });
    dialog.addEventListener('close', () => {
        pendingForm = undefined;
        pendingSubmitter = undefined;
    });
    dialog.dataset.confirmationReady = 'true';
}

function initializeDialogs(root = document) {
    root.querySelectorAll('[data-dialog-open]').forEach((trigger) => {
        if (trigger.dataset.dialogReady === 'true') {
            return;
        }

        const dialog = document.getElementById(trigger.dataset.dialogOpen);
        if (!(dialog instanceof HTMLDialogElement)) {
            return;
        }

        trigger.addEventListener('click', () => dialog.showModal());
        dialog.addEventListener('click', (event) => {
            if (event.target === dialog) {
                dialog.close();
            }
        });
        trigger.dataset.dialogReady = 'true';
    });
}

function initializeSpaceForum(root = document) {
    formatLocalTimes(root);
    initializeRequiredForms(root);
    initializeSubmittedPost(root);
    initializePostNavigators(root);
    initializePostPermalinks(root);
    initializeMarkdownComposers(root);
    initializePostReplyActions(root);
    initializeLanguageSwitches(root);
    initializeLiveTopics(root);
    initializeSearchSuggestions(root);
    initializeConfirmations(root);
    initializeDialogs(root);
    initializePreservedTopicPositions(root);
}

initializeSpaceForum();
document.addEventListener('DOMContentLoaded', () => initializeSpaceForum());
document.addEventListener('enhancedload', () => initializeSpaceForum());
