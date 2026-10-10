window.chronicleNavigation = (() => {
    const states = new WeakMap();
    const sessions = new Map();
    const bottomTolerance = 48;
    const awayDelay = 600;
    const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;
    const distanceFromBottom = el => Math.max(0, el.scrollHeight - el.clientHeight - el.scrollTop);
    const cancelAway = state => {
        if (state.awayTimer) clearTimeout(state.awayTimer);
        state.awayTimer = 0;
    };
    const showButton = (state, show) => {
        if (!state.button) return;
        state.button.dataset.visible = show ? 'true' : 'false';
        state.button.setAttribute('aria-hidden', show ? 'false' : 'true');
        state.button.tabIndex = show ? 0 : -1;
    };
    const save = state => {
        sessions.set(state.sessionKey, { scrollTop: state.el.scrollTop, manuallyScrolled: state.manuallyScrolled });
    };
    const setBottom = (state, behavior = 'instant') => {
        const target = Math.max(0, state.el.scrollHeight - state.el.clientHeight);
        if (Math.abs(target - state.el.scrollTop) < 2) return;
        state.lastScrollTop = target;
        if (behavior === 'smooth') {
            state.programmatic = true;
            state.el.scrollTo({ top: target, behavior });
            if (state.programmaticTimer) clearTimeout(state.programmaticTimer);
            state.programmaticTimer = setTimeout(() => {
                state.programmatic = false;
                state.programmaticTimer = 0;
                if (states.get(state.el) === state) evaluate(state, false);
            }, 500);
        } else {
            state.expectedScrollTop = target;
            state.el.scrollTop = target;
        }
    };
    const evaluate = (state, fromUserScroll) => {
        if (states.get(state.el) !== state) return;
        const distance = distanceFromBottom(state.el);
        if (fromUserScroll && !state.programmatic) {
            const movedUp = state.el.scrollTop < state.lastScrollTop - 1;
            state.lastScrollTop = state.el.scrollTop;
            if (movedUp) {
                state.manuallyScrolled = true;
                state.initialScrollPending = false;
            } else if (distance <= bottomTolerance) {
                // Deliberately returning to the bottom resumes following this response.
                state.manuallyScrolled = false;
            }
        } else {
            state.lastScrollTop = state.el.scrollTop;
        }
        state.lastDistanceFromBottom = distance;
        if (state.manuallyScrolled && distance >= state.el.clientHeight && state.el.clientHeight > 0) {
            if (!state.awayTimer) {
                state.awayTimer = setTimeout(() => {
                    state.awayTimer = 0;
                    if (states.get(state.el) === state && state.manuallyScrolled && distanceFromBottom(state.el) >= state.el.clientHeight)
                        showButton(state, true);
                }, awayDelay);
            }
        } else {
            cancelAway(state);
            showButton(state, false);
        }
        save(state);
    };
    const queueLayoutCheck = state => {
        if (state.layoutFrame || states.get(state.el) !== state) return;
        state.layoutFrame = requestAnimationFrame(() => {
            state.layoutFrame = 0;
            if (states.get(state.el) !== state) return;
            if (state.initialScrollPending && !state.manuallyScrolled) {
                state.initialScrollPending = false;
                setBottom(state);
            }
            // Keep the latest message anchored when the available Chronicle
            // viewport changes (for example, when the mobile composer grows).
            else if (state.followAfterViewportResize && !state.manuallyScrolled) {
                state.followAfterViewportResize = false;
                setBottom(state);
            }
            // Coalesce text/layout changes to one frame and follow only while enabled.
            else if (state.responseActive && !state.manuallyScrolled) setBottom(state);
            evaluate(state, false);
        });
    };
    const disconnect = state => {
        cancelAway(state);
        if (state.programmaticTimer) clearTimeout(state.programmaticTimer);
        if (state.layoutFrame) cancelAnimationFrame(state.layoutFrame);
        state.resizeObserver?.disconnect();
        state.viewportResizeObserver?.disconnect();
        state.mutationObserver?.disconnect();
        state.el.removeEventListener('scroll', state.onScroll);
        state.el.removeEventListener('wheel', state.onUserScrollIntent);
        state.el.removeEventListener('touchmove', state.onUserScrollIntent);
        state.el.removeEventListener('pointermove', state.onPointerScrollIntent);
        state.el.removeEventListener('keydown', state.onKeyScrollIntent);
        state.el.removeEventListener('load', state.onLoad, true);
        window.removeEventListener('resize', state.onResize);
        save(state);
        states.delete(state.el);
        showButton(state, false);
    };
    const initializePosition = (state, cached) => new Promise(resolve => {
        requestAnimationFrame(() => requestAnimationFrame(() => {
            if (states.get(state.el) !== state || !state.el.isConnected) { resolve(); return; }
            if (cached?.manuallyScrolled === true) {
                state.manuallyScrolled = true;
                state.el.scrollTop = Math.min(cached.scrollTop || 0, Math.max(0, state.el.scrollHeight - state.el.clientHeight));
            } else {
                state.manuallyScrolled = false;
                setBottom(state);
            }
            evaluate(state, false);
            save(state);
            state.dotnetRef?.invokeMethodAsync('ChronicleNavigationReady', state.sessionKey).catch(() => {});
            resolve();
        }));
    });
    const install = (el, button, dotnetRef, sessionKey, forceLatest) => {
        if (!el) return Promise.resolve();
        const existing = states.get(el);
        if (existing && existing.sessionKey === sessionKey) {
            if (forceLatest) {
                existing.manuallyScrolled = false;
                existing.initialScrollPending = false;
                cancelAway(existing);
                showButton(existing, false);
                return initializePosition(existing, { manuallyScrolled: false, scrollTop: 0 });
            }
            return Promise.resolve();
        }
        let cached = null;
        if (existing) {
            disconnect(existing);
            // An explicit Session switch always starts at that Session's latest message.
            cached = { manuallyScrolled: false, scrollTop: 0 };
        } else {
            cached = sessions.get(sessionKey) || null;
        }
        const state = {
            el, button, dotnetRef, sessionKey,
            manuallyScrolled: cached?.manuallyScrolled === true,
            initialScrollPending: false,
            programmatic: false, programmaticTimer: 0, awayTimer: 0, layoutFrame: 0,
            resizeObserver: null, viewportResizeObserver: null, mutationObserver: null,
            lastViewportHeight: el.clientHeight,
            lastDistanceFromBottom: distanceFromBottom(el),
            followAfterViewportResize: false
        };
        state.onScroll = () => {
            if (state.programmatic) { save(state); return; }
            if (state.expectedScrollTop !== undefined) {
                const expected = state.expectedScrollTop;
                state.expectedScrollTop = undefined;
                if (Math.abs(state.el.scrollTop - expected) < 2) { save(state); return; }
            }
            // Mobile browsers may adjust a nested scroller while opening the
            // keyboard. Only an actual scroll gesture should suspend follow.
            evaluate(state, performance.now() <= state.userScrollIntentUntil);
        };
        state.userScrollIntentUntil = 0;
        state.onUserScrollIntent = () => { state.userScrollIntentUntil = performance.now() + 500; };
        state.onPointerScrollIntent = event => { if (event.buttons !== 0) state.onUserScrollIntent(); };
        state.onKeyScrollIntent = event => {
            if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(event.key)) state.onUserScrollIntent();
        };
        state.onLoad = () => queueLayoutCheck(state);
        state.onResize = () => queueLayoutCheck(state);
        el.addEventListener('scroll', state.onScroll, { passive: true });
        el.addEventListener('wheel', state.onUserScrollIntent, { passive: true });
        el.addEventListener('touchmove', state.onUserScrollIntent, { passive: true });
        el.addEventListener('pointermove', state.onPointerScrollIntent, { passive: true });
        el.addEventListener('keydown', state.onKeyScrollIntent);
        el.addEventListener('load', state.onLoad, true);
        window.addEventListener('resize', state.onResize, { passive: true });
        const content = el.querySelector('.messages-content');
        if (typeof ResizeObserver !== 'undefined' && content) {
            state.resizeObserver = new ResizeObserver(() => {
                // A prose-size adjustment can change Chronicle height. Preserve the latest-message anchor only if this device was following immediately before the reflow.
                if (!state.manuallyScrolled && state.lastDistanceFromBottom <= bottomTolerance)
                    state.followAfterViewportResize = true;
                queueLayoutCheck(state);
            });
            state.resizeObserver.observe(content);
        }
        if (typeof ResizeObserver !== 'undefined') {
            state.viewportResizeObserver = new ResizeObserver(() => {
                const height = el.clientHeight;
                if (Math.abs(height - state.lastViewportHeight) < 1) return;
                state.lastViewportHeight = height;
                // Only preserve the bottom if this device was already there
                // before its visible Chronicle area changed size.
                if (!state.manuallyScrolled && state.lastDistanceFromBottom <= bottomTolerance)
                    state.followAfterViewportResize = true;
                queueLayoutCheck(state);
            });
            state.viewportResizeObserver.observe(el);
        }
        if (typeof MutationObserver !== 'undefined' && content) {
            state.mutationObserver = new MutationObserver(() => queueLayoutCheck(state));
            state.mutationObserver.observe(content, { childList: true, characterData: true, subtree: true });
        }
        states.set(el, state);
        state.lastScrollTop = el.scrollTop;
        return initializePosition(state, cached);
    };
    const suspend = el => {
        const state = el && states.get(el);
        if (state) disconnect(state);
    };
    const scrollToBottomOnce = el => {
        const state = el && states.get(el);
        if (state) setBottom(state);
    };
    const beginResponse = el => {
        const state = el && states.get(el);
        if (!state) return;
        if (state.layoutFrame) cancelAnimationFrame(state.layoutFrame);
        state.layoutFrame = 0;
        const wasActive = state.responseActive;
        state.responseActive = true;
        if (!wasActive && !state.manuallyScrolled) {
            state.initialScrollPending = true;
            cancelAway(state);
            showButton(state, false);
        }
        save(state);
        queueLayoutCheck(state);
    };
    const endResponse = el => {
        const state = el && states.get(el);
        if (!state) return;
        if (!state.manuallyScrolled) {
            state.initialScrollPending = false;
            setBottom(state);
        }
        state.responseActive = false;
        evaluate(state, false);
    };
    const jump = el => {
        const state = el && states.get(el);
        if (!state) return;
        cancelAway(state);
        state.initialScrollPending = false;
        showButton(state, false);
        setBottom(state, reducedMotion() ? 'instant' : 'smooth');
        save(state);
    };
    const composerResized = el => {
        const state = el && states.get(el);
        if (!state || state.manuallyScrolled) return;
        state.followAfterViewportResize = true;
        queueLayoutCheck(state);
    };
    return { install, suspend, scrollToBottomOnce, beginResponse, endResponse, jump, composerResized };
})();
window.installChronicleNavigation = (el, button, dotnetRef, sessionKey, forceLatest) => window.chronicleNavigation.install(el, button, dotnetRef, sessionKey, forceLatest);
window.suspendChronicleNavigation = el => window.chronicleNavigation.suspend(el);
window.scrollMessagesToBottom = el => window.chronicleNavigation.scrollToBottomOnce(el);
window.beginChronicleResponse = el => window.chronicleNavigation.beginResponse(el);
window.endChronicleResponse = el => window.chronicleNavigation.endResponse(el);
window.jumpChronicleToLatest = el => window.chronicleNavigation.jump(el);
;
// A live plain GM response uses one browser-owned container. Blazor supplies the
// current source text; this module appends only its unseen suffix and switches to
// DBLC's sanitized Markdown once when the response finishes.
window.chronicleStreamEffects = (() => {
    const states = new WeakMap();
    const disposedRoots = new WeakSet();
    const activeRoots = new Map();
    const diagnosticEnabled = () => {
        try { return new URLSearchParams(location.search).get('chronicleDiag') === '1' || sessionStorage.getItem('dblcChronicleDiagnostics') === '1'; }
        catch { return false; }
    };
    const trace = (root, state, event) => {
        const details = { event, segmentId: state.key, revision: state.revision, mode: state.mode,
            sourceChars: state.source?.length ?? 0, domChars: root.textContent?.length ?? 0,
            childNodes: root.childNodes?.length ?? root.children?.length ?? 0 };
        root.dataset.chronicleSegmentId = String(state.key ?? '');
        root.dataset.chronicleRenderRevision = String(state.revision ?? 0);
        root.dataset.chronicleRenderMode = String(state.mode ?? 'unknown');
        root.dataset.chronicleSourceChars = String(details.sourceChars);
        root.dataset.chronicleDomChars = String(details.domChars);
        root.dataset.chronicleNodeCount = String(details.childNodes);
        if (diagnosticEnabled()) console.debug('[DBLC Chronicle]', details);
    };
    const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;
    const clearSelectionWait = state => {
        if (state.selectionHandler) window.removeEventListener('selectionchange', state.selectionHandler);
        state.selectionHandler = null;
    };
    const ensureSelectionWait = (root, state) => {
        if (state.selectionHandler) return;
        state.selectionHandler = () => {
            if (state.disposed) return;
            if (state.finalHtml !== null) {
                if (!selectionWithin(root)) finish(root, state.finalHtml, state.key, state.revision, state);
                return;
            }
            const selection = window.getSelection?.();
            if (selection && !selection.isCollapsed && Array.from(state.pendingUnwrap).some(node => node.contains(selection.anchorNode) || node.contains(selection.focusNode))) return;
            for (const node of state.pendingUnwrap) if (node.isConnected) node.replaceWith(document.createTextNode(node.textContent || ''));
            state.pendingUnwrap.clear();
            clearSelectionWait(state);
        };
        window.addEventListener('selectionchange', state.selectionHandler);
    };
    const cancelPending = state => {
        if (state.frame) cancelAnimationFrame(state.frame);
        state.frame = 0;
        state.pending = [];
    };
    const appendBatch = (root, state, text, effect, animate) => {
        if (!text) return;
        if (!animate || reducedMotion()) { root.appendChild(document.createTextNode(text)); return; }
        const name = ['focus', 'shaky', '3d', 'vaporwave', 'smoke', 'hyperspace'].includes(effect) ? effect : 'focus';
        const batch = document.createElement('span');
        batch.className = 'chronicle-stream-effect-batch chronicle-stream-effect-' + name;
        batch.textContent = text;
        batch.addEventListener('animationend', () => {
            if (state.disposed || !batch.isConnected) return;
            const selection = window.getSelection?.();
            if (selection && !selection.isCollapsed && (batch.contains(selection.anchorNode) || batch.contains(selection.focusNode))) {
                state.pendingUnwrap ??= new Set();
                state.pendingUnwrap.add(batch);
                ensureSelectionWait(root, state);
                return;
            }
            batch.replaceWith(document.createTextNode(batch.textContent || ''));
        }, { once: true });
        root.appendChild(batch);
    };
    const flush = (root, state) => {
        state.frame = 0;
        if (state.disposed) return;
        const pending = state.pending;
        state.pending = [];
        for (const item of pending) appendBatch(root, state, item.text, item.effect, state.animate);
    };
    const appendDelta = (root, state, delta, start, effectSpans, defaultEffect) => {
        const end = start + delta.length;
        let position = start;
        const spans = effectSpans || [];
        while (position < end) {
            const span = spans.find(item => (item.start ?? item.Start) + (item.length ?? item.Length) > position);
            const spanStart = span ? (span.start ?? span.Start) : end;
            const spanEnd = span ? spanStart + (span.length ?? span.Length) : end;
            const effect = span ? (span.effect ?? span.Effect) : defaultEffect;
            const next = Math.min(end, span && spanStart <= position ? spanEnd : spanStart);
            const count = Math.max(1, next - position);
            const text = delta.slice(position - start, position - start + count);
            const last = state.pending[state.pending.length - 1];
            if (last && last.effect === effect) last.text += text;
            else state.pending.push({ text, effect });
            position += count;
        }
        if (state.pending.length && !state.frame) state.frame = requestAnimationFrame(() => flush(root, state));
    };
    const selectionWithin = root => {
        const selection = window.getSelection?.();
        return !!selection && !selection.isCollapsed && root.contains(selection.anchorNode) && root.contains(selection.focusNode);
    };
    const finish = (root, html, responseKey, revision, state) => {
        if (state.disposed || disposedRoots.has(root)) return;
        cancelPending(state);
        clearSelectionWait(state);
        if (root.id) activeRoots.delete(root.id);
        const template = document.createElement('template');
        template.innerHTML = String(html ?? ''); // Produced by ChronicleMarkdownRenderer's allowlist sanitizer.
        root.replaceChildren(template.content.cloneNode(true));
        root.classList.remove('chronicle-stream-source');
        if (responseKey) { state = { ...state, mode: 'static', key: responseKey, revision, source: '', pending: [], frame: 0, animate: false, pendingUnwrap: new Set(), selectionHandler: null, finalHtml: null }; states.set(root, state); trace(root, state, 'finish'); }
        else states.delete(root);
    };
    const sync = (root, responseKey, revision, fullText, animate, defaultEffect, effectSpans) => {
        if (!root || !responseKey || disposedRoots.has(root)) return;
        let state = states.get(root);
        if (state && Number(revision) < Number(state.revision ?? -1)) { trace(root, state, 'stale-sync-ignored'); return; }
        if (root.id) activeRoots.set(root.id, root);
        if (!state || state.mode !== 'stream' || state.key !== responseKey) {
            if (state) { cancelPending(state); clearSelectionWait(state); }
            root.replaceChildren();
            root.classList.add('chronicle-stream-source');
            state = { mode: 'stream', key: responseKey, revision: Number(revision), source: '', pending: [], frame: 0, animate, pendingUnwrap: new Set(), selectionHandler: null, finalHtml: null };
            states.set(root, state);
        }
        state.revision = Number(revision);
        if (state.finalHtml !== null) { state.finalHtml = null; if (!state.pendingUnwrap.size) clearSelectionWait(state); }
        state.animate = animate;
        const source = String(fullText ?? '');
        if (source.startsWith(state.source)) {
            const delta = source.slice(state.source.length);
            const start = state.source.length;
            state.source = source;
            if (delta) appendDelta(root, state, delta, start, effectSpans, defaultEffect);
            trace(root, state, delta ? 'sync-append' : 'sync-noop');
        } else {
            cancelPending(state);
            root.replaceChildren(document.createTextNode(source));
            state.source = source;
            trace(root, state, 'sync-correction');
        }
    };
    const render = (root, html, responseKey, revision) => {
        if (!root || disposedRoots.has(root)) return;
        const state = states.get(root);
        if (state && Number(revision) < Number(state.revision ?? -1)) { trace(root, state, 'stale-final-render-ignored'); return; }
        if (state?.mode === 'static' && state.key === responseKey && Number(revision) === Number(state.revision)) { trace(root, state, 'duplicate-final-render-ignored'); return; }
        if (state) { state.revision = Number(revision); state.key = responseKey; trace(root, state, 'final-render-request'); }
        if (state?.mode === 'stream') {
            if (state.frame) { cancelAnimationFrame(state.frame); state.frame = 0; flush(root, state); }
            if (selectionWithin(root)) {
                state.animate = false;
                state.finalHtml = html;
                ensureSelectionWait(root, state);
                return;
            }
        }
        finish(root, html, responseKey, revision, state || { pending: [], frame: 0 });
    };
    const dispose = rootOrId => {
        const root = typeof rootOrId === 'string' ? activeRoots.get(rootOrId) : rootOrId;
        if (!root || disposedRoots.has(root)) return;
        disposedRoots.add(root);
        if (root.id) activeRoots.delete(root.id);
        const state = states.get(root);
        if (!state) return;
        state.disposed = true;
        cancelPending(state);
        clearSelectionWait(state);
        state.pendingUnwrap.clear();
        state.finalHtml = null;
        states.delete(root);
    };
    return { sync, render, dispose };
})();
window.clickElementById = id => document.getElementById(id)?.click();
window.installMobileComposer = (form, textarea, messages) => {
    if (!form || !textarea || form.dataset.mobileComposerReady === 'true') return;
    form.dataset.mobileComposerReady = 'true';
    const onKeydown = event => {
        if (event.key !== 'Enter' || event.isComposing || event.keyCode === 229) return;
        event.preventDefault();
        const start = textarea.selectionStart ?? textarea.value.length;
        const end = textarea.selectionEnd ?? start;
        textarea.setRangeText('\n', start, end, 'end');
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
    };
    textarea.addEventListener('keydown', onKeydown);
    const resize = () => {
        const previousHeight = textarea.getBoundingClientRect().height;
        textarea.style.height = 'auto';
        const viewportHeight = window.visualViewport?.height || window.innerHeight;
        const max = Math.min(viewportHeight * 0.3, 240);
        textarea.style.height = `${Math.min(textarea.scrollHeight, max)}px`;
        if (Math.abs(textarea.getBoundingClientRect().height - previousHeight) > 1)
            window.chronicleNavigation?.composerResized(messages);
    };
    window.__dblcMobileComposerResize = resize;
    textarea.addEventListener('input', resize, { passive: true });
    resize();
};
(() => {
    if (window.__dblcMobileViewportInstalled) return;
    window.__dblcMobileViewportInstalled = true;
    const viewport = window.visualViewport;
    if (!viewport) return;
    let lastHeight = viewport.height;
    let lastOffsetTop = viewport.offsetTop;
    const update = () => {
        const height = viewport.height;
        const offsetTop = viewport.offsetTop;
        const geometryChanged = Math.abs(height - lastHeight) > 1 || Math.abs(offsetTop - lastOffsetTop) > 1;
        lastHeight = height;
        lastOffsetTop = offsetTop;
        document.documentElement.style.setProperty('--mobile-viewport-height', `${height}px`);
        document.documentElement.style.setProperty('--mobile-viewport-top', `${offsetTop}px`);
        window.__dblcMobileComposerResize?.();
        if (geometryChanged) window.chronicleNavigation?.composerResized(document.querySelector('.mobile-messages'));
    };
    viewport.addEventListener('resize', update, { passive: true });
    viewport.addEventListener('scroll', update, { passive: true });
    update();
})();
window.installComposerImageAttachments = (root, dotnetRef) => {
    if (!root || root.dataset.imageAttachmentReady === 'true') return;
    root.dataset.imageAttachmentReady = 'true';
    const maxBytes = 12 * 1024 * 1024;
    const isImageFile = file => file && (['image/png', 'image/jpeg'].includes((file.type || '').toLowerCase()) || /\.(png|jpe?g)$/i.test(file.name || ''));
    const reject = message => dotnetRef.invokeMethodAsync('RejectComposerImage', message);
    const send = file => {
        if (!isImageFile(file)) { reject('Choose a PNG or JPEG image.'); return; }
        if (file.size <= 0 || file.size > maxBytes) { reject('Choose one PNG or JPEG image smaller than 12 MB.'); return; }
        const reader = new FileReader();
        reader.onload = () => dotnetRef.invokeMethodAsync('ReceiveComposerImage', file.name || 'image.png', reader.result);
        reader.onerror = () => reject('DBLC could not read that image.');
        reader.readAsDataURL(file);
    };
    const hasFiles = event => event.dataTransfer?.types && Array.from(event.dataTransfer.types).includes('Files');
    root.addEventListener('dragover', event => {
        if (!hasFiles(event)) return;
        event.preventDefault();
        if (root.dataset.visionEnabled === 'true' && root.dataset.busy !== 'true') root.classList.add('image-drag-active');
    });
    root.addEventListener('dragenter', event => {
        if (hasFiles(event) && root.dataset.visionEnabled === 'true' && root.dataset.busy !== 'true') root.classList.add('image-drag-active');
    });
    root.addEventListener('dragleave', event => {
        if (!root.contains(event.relatedTarget)) root.classList.remove('image-drag-active');
    });
    root.addEventListener('drop', event => {
        if (!hasFiles(event)) return;
        event.preventDefault();
        root.classList.remove('image-drag-active');
        if (root.dataset.visionEnabled !== 'true' || root.dataset.busy === 'true') return;
        const file = Array.from(event.dataTransfer.files || []).find(isImageFile);
        if (file) send(file);
        else reject('Choose a PNG or JPEG image.');
    });
    root.addEventListener('paste', event => {
        const imageItem = Array.from(event.clipboardData?.items || []).find(item => ['image/png', 'image/jpeg'].includes((item.type || '').toLowerCase()));
        if (!imageItem) return;
        if (root.dataset.visionEnabled !== 'true' || root.dataset.busy === 'true') return;
        event.preventDefault();
        const file = imageItem.getAsFile();
        if (file) send(file);
    });
};
window.installComposerEnter = () => {
    if (document.documentElement.dataset.enterSubmitReady === 'true') return;
    document.documentElement.dataset.enterSubmitReady = 'true';
    document.addEventListener('keydown', event => {
        const textarea = event.target;
        if (!(textarea instanceof HTMLTextAreaElement) || !textarea.matches('.composer textarea')) return;
        if (event.key !== 'Enter' || event.shiftKey || event.isComposing || event.repeat) return;
        event.preventDefault();
        textarea.form?.requestSubmit();
    }, true);
};
window.installCleanGlitchCopy = () => {
    if (document.documentElement.dataset.cleanGlitchCopyReady === 'true') return;
    document.documentElement.dataset.cleanGlitchCopyReady = 'true';
    document.addEventListener('copy', event => {
        const selection = window.getSelection();
        if (!selection || selection.isCollapsed) return;
        const closestGlitch = node => (node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement)?.closest('.flavor-glitch');
        const start = closestGlitch(selection.anchorNode);
        const end = closestGlitch(selection.focusNode);
        const fragment = selection.rangeCount ? selection.getRangeAt(0).cloneContents() : null;
        const containsGlitch = !!fragment?.querySelector?.('.flavor-glitch');
        const glitch = start || end || (containsGlitch ? fragment.querySelector('.flavor-glitch') : null);
        if (!glitch) return;
        let clean = selection.toString();
        fragment?.querySelectorAll?.('.glitch-glyph[data-clean-glyph]').forEach(glyph => glyph.replaceWith(document.createTextNode(glyph.dataset.cleanGlyph || '')));
        if (fragment) {
            const toPlainText = node => {
                if (node.nodeType === Node.TEXT_NODE) return node.nodeValue || '';
                if (node.nodeType !== Node.ELEMENT_NODE && node.nodeType !== Node.DOCUMENT_FRAGMENT_NODE) return '';
                if (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'BR') return '\n';
                const content = Array.from(node.childNodes, toPlainText).join('');
                return node.nodeType === Node.ELEMENT_NODE && /^(DIV|P|LI)$/.test(node.tagName) ? `${content}\n` : content;
            };
            clean = toPlainText(fragment).replace(/\n+$/, '');
        }
        if (start && start === end && selection.toString() === start.innerText) clean = start.dataset.cleanText ?? clean;
        event.clipboardData?.setData('text/plain', clean);
        event.preventDefault();
    }, true);
};
window.clearComposer = (textarea) => { if (textarea) textarea.value = ''; };
window.installChronicleCopyMenu = () => {
    if (document.documentElement.dataset.chronicleCopyMenuReady === 'true') return;
    document.documentElement.dataset.chronicleCopyMenuReady = 'true';
    document.addEventListener('contextmenu', event => {
        if (!event.target.closest?.('.messages')) return;
        const selection = window.getSelection();
        if (!selection || selection.isCollapsed || !selection.toString().trim()) return;
        const savedRange = selection.getRangeAt(0).cloneRange();
        event.preventDefault();
        document.querySelector('.chronicle-copy-menu')?.remove();
        const menu = document.createElement('div');
        menu.className = 'chronicle-copy-menu';
        menu.setAttribute('role', 'menu');
        const copyButton = document.createElement('button');
        copyButton.type = 'button';
        copyButton.setAttribute('role', 'menuitem');
        copyButton.textContent = 'Copy';
        copyButton.addEventListener('click', () => {
            const currentSelection = window.getSelection();
            currentSelection?.removeAllRanges();
            currentSelection?.addRange(savedRange);
            document.execCommand('copy');
            menu.remove();
        });
        menu.appendChild(copyButton);
        document.body.appendChild(menu);
        const bounds = menu.getBoundingClientRect();
        menu.style.left = `${Math.max(4, Math.min(event.clientX, window.innerWidth - bounds.width - 4))}px`;
        menu.style.top = `${Math.max(4, Math.min(event.clientY, window.innerHeight - bounds.height - 4))}px`;
        const dismiss = e => { if (!menu.contains(e.target)) menu.remove(); };
        document.addEventListener('pointerdown', dismiss, { once: true, capture: true });
        document.addEventListener('keydown', function closeOnEscape(e) { if (e.key === 'Escape') { menu.remove(); document.removeEventListener('keydown', closeOnEscape); } }, { once: true });
    }, true);
};
window.enableSessionReorder = (dotnetRef) => {
    const list = document.querySelector('.conversation-list');
    if (!list || list.dataset.reorderReady === 'true') return;
    list.dataset.reorderReady = 'true';
    list.addEventListener('dragstart', (event) => {
        const row = event.target.closest('.session-row[draggable="true"]');
        if (!row) return;
        event.dataTransfer.setData('text/plain', row.dataset.sessionId || '');
        event.dataTransfer.effectAllowed = 'move';
    });
    list.addEventListener('dragover', (event) => {
        if (event.target.closest('.session-row[draggable="true"]')) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
        }
    });
    list.addEventListener('drop', (event) => {
        const row = event.target.closest('.session-row[draggable="true"]');
        if (!row) return;
        event.preventDefault();
        const draggedId = event.dataTransfer.getData('text/plain');
        const targetId = row.dataset.sessionId || '';
        const bounds = row.getBoundingClientRect();
        const placeAfter = event.clientY >= bounds.top + bounds.height / 2;
        if (draggedId && targetId) dotnetRef.invokeMethodAsync('ReorderSessionFromDrag', draggedId, targetId, placeAfter);
    });
};

window.playerMarkdownEditor = (() => {
    const states = new WeakMap();
    const imageLine = /^!\[([^\]]*)\]\((assets\/[A-Za-z0-9._-]+)\)$/;
    const maxFrameOffset = (image, frame, zoom, axis) => {
        const bounds = frame.getBoundingClientRect();
        if (!bounds.width || !bounds.height) return Math.max(0, (zoom - 1) * 50 / zoom);
        const aspect = image.naturalWidth && image.naturalHeight ? image.naturalWidth / image.naturalHeight : bounds.width / bounds.height;
        const coveredWidth = Math.min(bounds.width, bounds.height * aspect) * zoom;
        const coveredHeight = Math.min(bounds.height, bounds.width / aspect) * zoom;
        const size = axis === 'x' ? bounds.width : bounds.height;
        const covered = axis === 'x' ? coveredWidth : coveredHeight;
        return Math.max(0, (covered - size) / 2 / size * 100);
    };
    const applyFrame = (block) => {
        const img = block.querySelector('.player-edit-image-frame img');
        if (!img) return;
        const frame = img.parentElement, zoom = Number(block.dataset.zoom || 1);
        const xLimit = maxFrameOffset(img, frame, zoom, 'x'), yLimit = maxFrameOffset(img, frame, zoom, 'y');
        block.dataset.x = Math.max(-xLimit, Math.min(xLimit, Number(block.dataset.x || 0)));
        block.dataset.y = Math.max(-yLimit, Math.min(yLimit, Number(block.dataset.y || 0)));
        img.style.objectPosition = '50% 50%';
        img.style.transform = 'translate(' + block.dataset.x + '%,' + block.dataset.y + '%) scale(' + zoom + ')';
        const slider = block.querySelector('.player-edit-positioner input');
        if (slider) slider.value = block.dataset.zoom || 1;
    };
    const imageBlock = (asset) => {
        const block = document.createElement('div');
        block.className = 'player-edit-line player-image-block'; block.contentEditable = 'false'; block.draggable = true;
        block.dataset.path = asset.markdownPath; block.dataset.alt = asset.alt || ''; block.dataset.blockId = crypto.randomUUID();
        const frame = asset.frame || { x: 0, y: 0, zoom: 1 };
        block.dataset.x = frame.x || 0; block.dataset.y = frame.y || 0; block.dataset.zoom = frame.zoom || 1;
        const preview = document.createElement('div'); preview.className = 'player-edit-image-frame';
        const img = document.createElement('img'); img.src = asset.dataUrl; img.alt = asset.alt || ''; img.draggable = false; preview.append(img);
        const caption = document.createElement('div'); caption.className = 'player-edit-image-caption'; caption.textContent = asset.alt || 'Image';
        const controls = document.createElement('div'); controls.className = 'player-edit-image-controls';
        controls.innerHTML = '<button type="button" data-image-action="chronicle-portrait">Set as Chronicle Portrait</button><button type="button" data-image-action="position">Position</button><button type="button" data-image-action="remove">Remove</button>';
        const positioner = document.createElement('div'); positioner.className = 'player-edit-positioner';
        positioner.innerHTML = '<span>Drag image within frame</span><label>Zoom <input type="range" min="1" max="8" step="0.05"></label>';
        positioner.querySelector('input').value = block.dataset.zoom;
        block.append(preview, caption, controls, positioner);
        img.addEventListener('load', () => applyFrame(block), { once: true });
        return block;
    };
    const lineNode = (text) => { const line = document.createElement('div'); line.className = 'player-edit-line'; if (text) line.textContent = text; else line.append(document.createElement('br')); return line; };
    const render = (root, state, markdown, assets) => {
        state.assets = new Map(Object.entries(assets || {})); root.replaceChildren();
        const lines = (markdown || '').split('\n');
        for (const line of lines) { const match = imageLine.exec(line); const asset = match && state.assets.get(match[2]); const node = asset ? imageBlock({ ...asset, alt: match[1] }) : lineNode(line); root.append(node); if (asset) applyFrame(node); }
        state.lastMarkdown = markdown || '';
    };
    const lineText = (node) => {
        if (node.tagName === 'BR') return '';
        let text = '';
        for (const child of node.childNodes) {
            if (child.nodeType === Node.TEXT_NODE) text += child.nodeValue || '';
            else if (child.nodeType === Node.ELEMENT_NODE) {
                if (child.tagName === 'BR') text += '\n';
                else { text += lineText(child); if (/^(DIV|P)$/.test(child.tagName)) text += '\n'; }
            }
        }
        return text;
    };
    const serialize = (root) => Array.from(root.children).map(node => node.classList.contains('player-image-block') ? '![' + (node.dataset.alt || '') + '](' + node.dataset.path + ')' : lineText(node).replace(/\n$/, '')).join('\n');
    const emit = (root, state) => { const markdown = serialize(root); if (markdown === state.lastMarkdown) return; state.lastMarkdown = markdown; state.dotnet.invokeMethodAsync('MarkdownEdited', markdown); };
    const caretAtPoint = (x, y) => {
        if (document.caretRangeFromPoint) return document.caretRangeFromPoint(x, y);
        const point = document.caretPositionFromPoint?.(x, y); if (!point) return null;
        const range = document.createRange(); range.setStart(point.offsetNode, point.offset); range.collapse(true); return range;
    };
    const insertAtRange = (root, range, block) => {
        const container = range?.startContainer;
        const line = container?.nodeType === Node.ELEMENT_NODE ? container.closest?.('.player-edit-line') : container?.parentElement?.closest('.player-edit-line');
        if (line && root.contains(line) && !line.classList.contains('player-image-block')) {
            const beforeRange = document.createRange(); beforeRange.selectNodeContents(line); beforeRange.setEnd(range.startContainer, range.startOffset);
            const afterRange = document.createRange(); afterRange.selectNodeContents(line); afterRange.setStart(range.startContainer, range.startOffset);
            const before = beforeRange.cloneContents(); const after = afterRange.cloneContents();
            line.replaceChildren(); line.append(before); const afterLine = lineNode(''); afterLine.replaceChildren(after); line.after(block, afterLine); return;
        }
        const target = container?.nodeType === Node.ELEMENT_NODE ? container.closest?.('.player-image-block') : container?.parentElement?.closest('.player-image-block');
        if (target && root.contains(target)) target.after(block); else root.append(block);
    };
    const addImageFile = async (root, state, file, range, existingPending = null) => {
        const pending = existingPending || document.createElement('div');
        if (!existingPending) { pending.className = 'player-edit-line player-image-pending'; pending.contentEditable = 'false'; pending.textContent = 'Adding image…'; insertAtRange(root, range, pending); }
        try {
            const bytes = new Uint8Array(await file.arrayBuffer()); let binary = '';
            for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
            const asset = await state.dotnet.invokeMethodAsync('ImageDropped', file.name, btoa(binary));
            state.assets.set(asset.markdownPath, asset); const block = imageBlock(asset); pending.replaceWith(block); applyFrame(block); emit(root, state);
        } catch (error) { pending.remove(); state.dotnet.invokeMethodAsync('ImageDropFailed', error?.message || 'Could not add this image.'); }
    };
    const saveFrame = (state, block) => state.dotnet.invokeMethodAsync('ImageFrameChanged', block.dataset.path, Number(block.dataset.x), Number(block.dataset.y), Number(block.dataset.zoom));
    const initialize = (root, dotnet, markdown, assets) => {
        const state = { dotnet, assets: new Map(), range: null, lastMarkdown: '', abort: new AbortController(), moving: null, positioning: null, pointer: null };
        states.set(root, state); root.contentEditable = 'true'; root.spellcheck = false; render(root, state, markdown, assets);
        const opts = { signal: state.abort.signal };
        const remember = () => { const selection = getSelection(); if (selection?.rangeCount && root.contains(selection.anchorNode)) state.range = selection.getRangeAt(0).cloneRange(); };
        root.addEventListener('keyup', remember, opts); root.addEventListener('mouseup', remember, opts);
        root.addEventListener('input', (event) => {
            if (event.target.matches('.player-edit-positioner input')) { const block = event.target.closest('.player-image-block'); block.dataset.zoom = event.target.value; applyFrame(block); saveFrame(state, block); return; }
            remember(); emit(root, state);
        }, opts);
        root.addEventListener('click', (event) => {
            const button = event.target.closest('[data-image-action]');
            if (button) { event.preventDefault(); event.stopPropagation(); const block = button.closest('.player-image-block'); if (button.dataset.imageAction === 'remove') { block.remove(); emit(root, state); } else if (button.dataset.imageAction === 'chronicle-portrait') { state.dotnet.invokeMethodAsync('SetChroniclePortrait', block.dataset.path); } else { block.classList.toggle('positioning'); block.draggable = !block.classList.contains('positioning'); } return; }
            const block = event.target.closest('.player-image-block'); root.querySelectorAll('.player-image-block.selected').forEach(item => { if (item !== block) item.classList.remove('selected'); }); block?.classList.toggle('selected');
        }, opts);
        root.addEventListener('dragstart', event => { const block = event.target.closest('.player-image-block'); if (!block || block.classList.contains('positioning')) { event.preventDefault(); return; } state.moving = block; event.dataTransfer.setData('text/plain', 'DBLC_IMAGE:' + block.dataset.blockId); event.dataTransfer.effectAllowed = 'move'; }, opts);
        root.addEventListener('dragover', event => { if (state.moving || Array.from(event.dataTransfer?.types || []).includes('Files')) { event.preventDefault(); event.dataTransfer.dropEffect = state.moving ? 'move' : 'copy'; } }, opts);
        root.addEventListener('drop', event => {
            const files = Array.from(event.dataTransfer?.files || []);
            if (files.length) {
                event.preventDefault();
                const target = event.target.closest('.player-edit-line');
                if (target?.classList.contains('player-image-block')) {
                    const pending = document.createElement('div'); pending.className = 'player-edit-line player-image-pending'; pending.contentEditable = 'false'; pending.textContent = 'Adding image…';
                    target.parentNode.insertBefore(pending, event.clientY > target.getBoundingClientRect().top + target.getBoundingClientRect().height / 2 ? target.nextSibling : target);
                    addImageFile(root, state, files[0], null, pending);
                } else addImageFile(root, state, files[0], caretAtPoint(event.clientX, event.clientY) || state.range?.cloneRange());
                return;
            }
            if (!state.moving) return;
            event.preventDefault(); const target = event.target.closest('.player-edit-line');
            if (target && target !== state.moving) { const rect = target.getBoundingClientRect(); target.parentNode.insertBefore(state.moving, event.clientY > rect.top + rect.height / 2 ? target.nextSibling : target); emit(root, state); }
            state.moving = null;
        }, opts);
        root.addEventListener('dragend', () => { state.moving = null; }, opts);
        root.addEventListener('paste', event => {
            const item = Array.from(event.clipboardData?.items || []).find(entry => entry.kind === 'file' && entry.type.startsWith('image/'));
            if (!item) return; const file = item.getAsFile(); if (!file) return;
            event.preventDefault(); addImageFile(root, state, file, state.range?.cloneRange() || getSelection()?.getRangeAt(0)?.cloneRange());
        }, opts);
        root.addEventListener('pointerdown', event => {
            const img = event.target.closest('.player-edit-image-frame img'); const block = img?.closest('.player-image-block');
            if (!img || !block?.classList.contains('positioning')) return;
            event.preventDefault(); event.stopPropagation(); state.positioning = block; state.pointer = { x: event.clientX, y: event.clientY, ox: Number(block.dataset.x), oy: Number(block.dataset.y) }; img.setPointerCapture(event.pointerId);
        }, opts);
        root.addEventListener('pointermove', event => {
            if (!state.positioning || !state.pointer) return;
            const frame = state.positioning.querySelector('.player-edit-image-frame'); const bounds = frame.getBoundingClientRect();
            const img = frame.querySelector('img'), zoom = Number(state.positioning.dataset.zoom || 1);
            const xLimit = maxFrameOffset(img, frame, zoom, 'x'), yLimit = maxFrameOffset(img, frame, zoom, 'y');
            state.positioning.dataset.x = Math.max(-xLimit, Math.min(xLimit, state.pointer.ox + (event.clientX - state.pointer.x) * 100 / bounds.width));
            state.positioning.dataset.y = Math.max(-yLimit, Math.min(yLimit, state.pointer.oy + (event.clientY - state.pointer.y) * 100 / bounds.height)); applyFrame(state.positioning);
        }, opts);
        root.addEventListener('pointerup', () => { if (state.positioning) saveFrame(state, state.positioning); state.positioning = null; state.pointer = null; }, opts);
    };
    const update = (root, markdown, assets) => { const state = states.get(root); if (state) render(root, state, markdown, assets); };
    const insertAtSelection = (root, asset) => { const state = states.get(root); if (!state) return; state.assets.set(asset.markdownPath, asset); insertAtRange(root, state.range?.cloneRange(), imageBlock(asset)); emit(root, state); };
    const dispose = (root) => { const state = states.get(root); state?.abort.abort(); states.delete(root); };
    return { initialize, update, insertAtSelection, dispose };
})();

window.bindPlayerMarkdownPreview = (root, dotnetRef) => {
    if (!root) return;
    if (root.dataset.imageClickReady !== 'true') {
        root.dataset.imageClickReady = 'true';
        root.addEventListener('click', event => { const image = event.target.closest('.player-markdown-image'); if (image) dotnetRef.invokeMethodAsync('OpenPlayerImage', image.dataset.imagePath); });
    }
    root.querySelectorAll('.player-markdown-image-frame img').forEach(image => {
        const apply = () => {
            const frame = image.parentElement, bounds = frame.getBoundingClientRect();
            if (!bounds.width || !bounds.height) return;
            const zoom = Number(frame.style.getPropertyValue('--frame-zoom') || 1);
            const x = Number.parseFloat(frame.style.getPropertyValue('--frame-x')) || 0;
            const y = Number.parseFloat(frame.style.getPropertyValue('--frame-y')) || 0;
            const aspect = image.naturalWidth && image.naturalHeight ? image.naturalWidth / image.naturalHeight : bounds.width / bounds.height;
            const coveredWidth = Math.min(bounds.width, bounds.height * aspect) * zoom;
            const coveredHeight = Math.min(bounds.height, bounds.width / aspect) * zoom;
            const xLimit = Math.max(0, (coveredWidth - bounds.width) / 2 / bounds.width * 100);
            const yLimit = Math.max(0, (coveredHeight - bounds.height) / 2 / bounds.height * 100);
            image.style.transform = 'translate(' + Math.max(-xLimit, Math.min(xLimit, x)) + '%,' + Math.max(-yLimit, Math.min(yLimit, y)) + '%) scale(' + zoom + ')';
        };
        if (image.complete) apply(); else image.addEventListener('load', apply, { once: true });
    });
};

window.npcPortraitPositioner = (() => {
    const bound = new WeakSet();
    const maxFrameOffset = (image, frame, zoom, axis) => {
        const bounds = frame.getBoundingClientRect();
        if (!bounds.width || !bounds.height) return 0;
        const aspect = image.naturalWidth && image.naturalHeight ? image.naturalWidth / image.naturalHeight : bounds.width / bounds.height;
        const coveredWidth = Math.min(bounds.width, bounds.height * aspect) * zoom;
        const coveredHeight = Math.min(bounds.height, bounds.width / aspect) * zoom;
        const size = axis === 'x' ? bounds.width : bounds.height;
        const covered = axis === 'x' ? coveredWidth : coveredHeight;
        return Math.max(0, (covered - size) / 2 / size * 100);
    };
    const clamp = (value, limit) => Math.max(-limit, Math.min(limit, value));
    const initialize = (frame, dotnet) => {
        if (!frame || bound.has(frame)) return;
        bound.add(frame);
        const initialImage = frame.querySelector('.npc-portrait-image');
        if (initialImage?.complete) {
            const zoom = Number(frame.dataset.zoom || 1), xLimit = maxFrameOffset(initialImage, frame, zoom, 'x'), yLimit = maxFrameOffset(initialImage, frame, zoom, 'y');
            frame.dataset.x = clamp(Number(frame.dataset.x || 0), xLimit);
            frame.dataset.y = clamp(Number(frame.dataset.y || 0), yLimit);
            initialImage.style.transform = 'translate(' + frame.dataset.x + '%,' + frame.dataset.y + '%) scale(' + zoom + ')';
        }
        frame.addEventListener('pointerdown', event => {
            const image = event.target.closest('.npc-portrait-image');
            if (!image) return;
            event.preventDefault();
            const bounds = frame.getBoundingClientRect();
            const zoom = Number(frame.dataset.zoom || 1);
            const drag = { pointerId: event.pointerId, image, x: event.clientX, y: event.clientY, originX: Number(frame.dataset.x || 0), originY: Number(frame.dataset.y || 0), width: bounds.width, height: bounds.height, xLimit: maxFrameOffset(image, frame, zoom, 'x'), yLimit: maxFrameOffset(image, frame, zoom, 'y'), zoom };
            const move = moveEvent => {
                if (moveEvent.pointerId !== drag.pointerId) return;
                const x = clamp(drag.originX + (moveEvent.clientX - drag.x) * 100 / drag.width, drag.xLimit);
                const y = clamp(drag.originY + (moveEvent.clientY - drag.y) * 100 / drag.height, drag.yLimit);
                frame.dataset.x = x;
                frame.dataset.y = y;
                image.style.objectPosition = '50% 50%';
                image.style.transform = 'translate(' + x + '%,' + y + '%) scale(' + drag.zoom + ')';
            };
            const finish = finishEvent => {
                if (finishEvent.pointerId !== drag.pointerId) return;
                window.removeEventListener('pointermove', move);
                window.removeEventListener('pointerup', finish);
                window.removeEventListener('pointercancel', finish);
                dotnet.invokeMethodAsync('NpcPortraitFrameChanged', Number(frame.dataset.x || 0), Number(frame.dataset.y || 0));
            };
            window.addEventListener('pointermove', move);
            window.addEventListener('pointerup', finish);
            window.addEventListener('pointercancel', finish);
        });
    };
    return { initialize };
})();

window.installMobileSessionLongPress = (root, dotnet) => {
    if (!root || root.dataset.sessionLongPressReady === 'true') return;
    root.dataset.sessionLongPressReady = 'true';
    let timer = 0, startX = 0, startY = 0, activeRow = null;
    const cancel = () => { if (timer) window.clearTimeout(timer); timer = 0; activeRow = null; };
    root.addEventListener('pointerdown', event => {
        if (event.pointerType === 'mouse' || event.target.closest('.mobile-session-options')) return;
        const row = event.target.closest('[data-mobile-session-id]');
        if (!row || !root.contains(row)) return;
        cancel();
        activeRow = row; startX = event.clientX; startY = event.clientY;
        const sessionId = row.dataset.mobileSessionId;
        timer = window.setTimeout(() => {
            timer = 0;
            row.dataset.suppressNextSessionClick = 'true';
            dotnet.invokeMethodAsync('OpenMobileSessionMenuFromGesture', sessionId);
        }, 550);
    }, { passive: true });
    root.addEventListener('pointermove', event => {
        if (timer && (Math.abs(event.clientX - startX) > 12 || Math.abs(event.clientY - startY) > 12)) cancel();
    }, { passive: true });
    root.addEventListener('pointerup', cancel, { passive: true });
    root.addEventListener('pointercancel', cancel, { passive: true });
    root.addEventListener('pointerleave', cancel, { passive: true });
    root.addEventListener('click', event => {
        const row = event.target.closest('[data-mobile-session-id]');
        if (row?.dataset.suppressNextSessionClick !== 'true') return;
        delete row.dataset.suppressNextSessionClick;
        event.preventDefault();
        event.stopImmediatePropagation();
    }, true);
    root.addEventListener('contextmenu', event => {
        const row = event.target.closest('[data-mobile-session-id]');
        if (!row || !root.contains(row)) return;
        event.preventDefault();
        dotnet.invokeMethodAsync('OpenMobileSessionMenuFromGesture', row.dataset.mobileSessionId);
    });
};

window.downloadTextFile = (fileName, content, mimeType) => {
    const blob = new Blob([content], { type: mimeType || 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
};

// Pointer gestures stay local; opening the menu does not wait for the server.
window.diceControls = (() => {
    const states = new WeakMap();
    const install = (root, dotnet) => {
        if (!root || states.has(root)) return;
        const button = root.querySelector('.die-roll-button'), menu = root.querySelector('.die-picker');
        const abort = new AbortController();
        let press = null, timer = 0, pending = false, suppressChoiceClick = false;
        const compact = root.dataset.compact === 'true';
        const clearTimer = () => { clearTimeout(timer); timer = 0; button.classList.remove('charging'); };
        const close = () => { clearTimer(); press = null; menu.hidden = true; button.classList.remove('charged'); button.setAttribute('aria-expanded', 'false'); };
        const open = () => { menu.hidden = false; button.classList.add('charged'); button.setAttribute('aria-expanded', 'true'); };
        const act = (sides, roll, session = root.dataset.session) => {
            if (button.disabled || pending || !session) return;
            close(); pending = true;
            dotnet.invokeMethodAsync('DiceAction', sides, roll, crypto.randomUUID(), session)
                .catch(() => { /* A disconnected circuit must not retry a durable roll automatically. */ })
                .finally(() => { pending = false; });
        };
        const selectedSides = () => Number(root.querySelector('.die-choice.selected')?.dataset.sides || 20);
        const onDown = event => {
            if (event.button !== 0 || button.disabled || pending) return;
            event.preventDefault();
            if (!menu.hidden) { close(); return; }
            press = { id: event.pointerId, x: event.clientX, y: event.clientY, session: root.dataset.session, held: false };
            button.classList.add('charging');
            timer = setTimeout(() => { timer = 0; button.classList.remove('charging'); if (press && !button.disabled) { press.held = true; open(); } }, 720);
        };
        const onMove = event => {
            if (!press || press.id !== event.pointerId || press.held) return;
            if (Math.hypot(event.clientX - press.x, event.clientY - press.y) > 12) { clearTimer(); press = null; }
        };
        const onUp = event => {
            if (!press || press.id !== event.pointerId) return;
            const gesture = press; press = null; clearTimer();
            if (button.disabled || gesture.session !== root.dataset.session) { close(); return; }
            if (gesture.held) {
                const choice = document.elementFromPoint(event.clientX, event.clientY)?.closest('.die-choice');
                if (!compact && choice && menu.contains(choice)) {
                    suppressChoiceClick = true; setTimeout(() => { suppressChoiceClick = false; }, 0);
                    act(Number(choice.dataset.sides), true, gesture.session);
                }
                // Release outside a choice keeps the menu open; touch long-press release never rolls.
            } else if (button.contains(event.target)) act(selectedSides(), true, gesture.session);
        };
        const onChoice = event => {
            const choice = event.target.closest('.die-choice');
            if (!choice || !menu.contains(choice)) return;
            event.preventDefault();
            if (!suppressChoiceClick) act(Number(choice.dataset.sides), false);
        };
        const onOutside = event => { if (!root.contains(event.target)) close(); };
        const onKey = event => {
            if (event.key === 'Escape') { if (!menu.hidden) { close(); button.focus(); } return; }
            if (event.target === button && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
                event.preventDefault(); if (!button.disabled) { open(); menu.querySelector('.selected')?.focus(); }
            }
        };
        const listen = (target, name, callback) => target.addEventListener(name, callback, { signal: abort.signal });
        listen(button, 'pointerdown', onDown);
        listen(button, 'click', event => { event.preventDefault(); if (event.detail === 0 && !pending) { if (!menu.hidden) close(); else act(selectedSides(), true); } });
        listen(button, 'contextmenu', event => event.preventDefault());
        listen(menu, 'click', onChoice);
        listen(document, 'pointerdown', onOutside);
        listen(document, 'pointermove', onMove);
        listen(document, 'pointerup', onUp);
        listen(document, 'pointercancel', () => { clearTimer(); press = null; });
        listen(document, 'keydown', onKey);
        states.set(root, { dispose: () => { close(); abort.abort(); } });
    };
    return { install, dispose: root => { states.get(root)?.dispose(); states.delete(root); } };
})();
