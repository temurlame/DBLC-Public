import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../../src/DBLC/wwwroot/app.js', import.meta.url), 'utf8');
const start = source.indexOf('window.chronicleStreamEffects = (() => {');
const end = source.indexOf('})();', start) + 4;
assert.ok(start >= 0 && end > start, 'chronicleStreamEffects implementation exists');

class Node {
    constructor(type, text = '') {
        this.type = type;
        this.text = text;
        this.children = [];
        this.classes = new Set();
        this.listeners = {};
        this.dataset = {};
        this.connected = true;
        this.classList = { add: name => this.classes.add(name), remove: name => this.classes.delete(name) };
    }
    set className(value) { this.classes = new Set(String(value).split(/\s+/).filter(Boolean)); }
    appendChild(child) { child.parent = this; this.children.push(child); return child; }
    replaceChildren(...children) { this.children.forEach(child => child.connected = false); this.children = []; children.forEach(child => this.appendChild(child)); }
    replaceWith(replacement) {
        if (!this.parent) return;
        const index = this.parent.children.indexOf(this);
        replacement.parent = this.parent;
        this.parent.children.splice(index, 1, replacement);
        this.connected = false;
    }
    contains(node) { return node === this || this.children.some(child => child.contains(node)); }
    addEventListener(name, callback) { this.listeners[name] = callback; }
    get isConnected() { return this.connected; }
    get childNodes() { return this.children; }
    get textContent() { return this.children.length ? this.children.map(child => child.textContent).join('') : this.text; }
    set textContent(value) { this.text = value; this.children = []; }
    cloneNode() { return new Node(this.type, this.text); }
}

function setup(reduced = false) {
    const frames = new Map();
    const listeners = new Map();
    const selection = { isCollapsed: true, anchorNode: null, focusNode: null };
    let frameId = 0;
    const context = {
        window: {
            matchMedia: () => ({ matches: reduced }),
            getSelection: () => selection,
            addEventListener: (name, handler) => {
                if (!listeners.has(name)) listeners.set(name, new Set());
                listeners.get(name).add(handler);
            },
            removeEventListener: (name, handler) => listeners.get(name)?.delete(handler)
        },
        document: {
            createTextNode: text => new Node('text', text),
            createElement: name => {
                const node = new Node(name);
                if (name === 'template') {
                    node.content = { cloneNode: () => new Node('html', node.innerHTML) };
                    Object.defineProperty(node, 'innerHTML', { set(value) { this.text = value; }, get() { return this.text; } });
                }
                return node;
            }
        },
        requestAnimationFrame: callback => { const id = ++frameId; frames.set(id, callback); return id; },
        cancelAnimationFrame: id => frames.delete(id)
    };
    vm.runInNewContext(source.slice(start, end), context);
    return { effects: context.window.chronicleStreamEffects, root: new Node('div'), selection,
        flush: () => { const pending = [...frames.values()]; frames.clear(); pending.forEach(callback => callback()); },
        pendingFrame: () => [...frames.values()][0],
        selectionListeners: () => [...(listeners.get('selectionchange') ?? [])],
        selectionChanged: () => { for (const handler of [...(listeners.get('selectionchange') ?? [])]) handler(); } };
}

test('appends only new clean batches with their selected effects, then hands off to Markdown', () => {
    const { effects, root, flush } = setup();
    const key = 'response-1';
    const first = 'The first phrase.';
    effects.sync(root, key, 1, first, true, 'focus', [{ start: 0, length: first.length, effect: 'shaky' }]);
    flush();
    const firstBatch = root.children[0];
    assert.ok(firstBatch.classes.has('chronicle-stream-effect-shaky'));
    assert.equal(root.textContent, first);

    const finalSource = first + ' The second phrase.';
    effects.sync(root, key, 2, finalSource, true, 'focus', [
        { start: 0, length: first.length, effect: 'shaky' },
        { start: first.length, length: finalSource.length - first.length, effect: '3d' }
    ]);
    assert.equal(root.children[0], firstBatch, 'existing DOM batch remains mounted and stationary');
    flush();
    assert.equal(root.children[0], firstBatch, 'existing DOM batch is not recreated');
    assert.ok(root.children[1].classes.has('chronicle-stream-effect-3d'));
    assert.equal(root.textContent, finalSource, 'source remains ordered and is not duplicated');

    firstBatch.listeners.animationend();
    assert.equal(root.children[0].type, 'text', 'finished animation wrapper is removed');
    effects.render(root, '<p><strong>The first phrase.</strong> The second phrase.</p>', key, 3);
    assert.equal(root.children.length, 1);
    assert.equal(root.children[0].text, '<p><strong>The first phrase.</strong> The second phrase.</p>');
});

test('corrections resync without animation and reduced motion appends plain text', () => {
    const regular = setup();
    regular.effects.sync(regular.root, 'response-2', 1, 'partial prefix', true, 'focus', []);
    regular.flush();
    regular.effects.sync(regular.root, 'response-2', 2, 'replacement after tool cleanup', true, 'focus', []);
    assert.equal(regular.root.textContent, 'replacement after tool cleanup');
    assert.equal(regular.root.children[0].type, 'text');

    const reduced = setup(true);
    reduced.effects.sync(reduced.root, 'response-3', 1, 'plain readable text', true, 'hyperspace', []);
    reduced.flush();
    assert.equal(reduced.root.children[0].type, 'text');
    assert.equal(reduced.root.textContent, 'plain readable text');
});

test('maps each supported presentation effect to its Chronicle animation class', () => {
    const styles = readFileSync(new URL('../../src/DBLC/wwwroot/css/app.css', import.meta.url), 'utf8');
    for (const effect of ['focus', 'shaky', '3d', 'vaporwave', 'smoke', 'hyperspace']) {
        const { effects, root, flush } = setup();
        const text = 'New narration';
        effects.sync(root, 'effect-' + effect, 1, text, true, 'focus', [{ start: 0, length: text.length, effect }]);
        flush();
        assert.ok(root.children[0].classes.has('chronicle-stream-effect-' + effect), effect + ' applies its Chronicle class');
        assert.ok(styles.includes('.chronicle-stream-effect-' + effect + '{animation-name:'), effect + ' has a CSS animation');
    }
    assert.match(styles, /@media\(max-width:700px\)\{[^}]*\.chronicle-stream-effect-batch\{animation-duration:420ms\}/, 'mobile gets a longer, visible one-shot duration');
    assert.match(styles, /\.chronicle-stream-effect-smoke\{animation-duration:600ms\}/, 'mobile smoke effect remains distinct');
    assert.match(styles, /@media\(prefers-reduced-motion:reduce\)[\s\S]*?\.chronicle-stream-effect-batch\{animation:none!important/, 'reduced-motion preference still disables effects');
});

test('speaker segment streaming hands off once and ignores a delayed stale stream update', () => {
    const { effects, root, flush } = setup();
    const key = 'line-opaque-segment-id';
    const opening = 'The lantern swings. ';
    effects.sync(root, key, 1, opening, true, 'focus', []);
    flush();
    assert.equal(root.textContent, opening, 'initial GM narration is present in the live DOM');

    const speakerText = 'Example NPC studies your face. She lowers her voice. A third sentence follows.';
    for (let i = 0; i < speakerText.length; i += 7) {
        effects.sync(root, key, 2 + i, opening + speakerText.slice(0, i + 7), true, 'focus', []);
        flush();
    }
    const completeSource = opening + speakerText;
    assert.equal(root.textContent, completeSource, 'speaker body remains complete after rapid incremental updates');

    effects.render(root, '<p>' + opening + '</p><p>' + speakerText + '</p>', key, 100);
    assert.equal(root.dataset.chronicleRenderMode, 'static');
    assert.equal(root.dataset.chronicleSourceChars, '0');
    assert.equal(root.dataset.chronicleDomChars, String(root.textContent.length), 'final Markdown DOM count is captured without narration');
    assert.ok(root.textContent.includes(speakerText), 'final Markdown retains the complete speaker body');
    const completedMarkup = root.textContent;

    effects.sync(root, key, 99, opening + 'stale shorter response', true, 'focus', []);
    assert.equal(root.textContent, completedMarkup, 'an out-of-order stream callback cannot replace completed Markdown');
    assert.equal(root.dataset.chronicleRenderMode, 'static');
});

test('selection during animation still completes deferred Markdown exactly once', () => {
    for (const animationEndsBeforeCompletion of [true, false]) {
        const { effects, root, flush, selection, selectionChanged } = setup();
        const key = 'selected-response-' + animationEndsBeforeCompletion;
        const sourceText = '**A finished sentence.**';
        const markdown = '<p><strong>A finished sentence.</strong></p>';
        effects.sync(root, key, 1, sourceText, true, 'focus', []);
        flush();
        const batch = root.children[0];
        selection.isCollapsed = false;
        selection.anchorNode = batch;
        selection.focusNode = batch;
        if (animationEndsBeforeCompletion) batch.listeners.animationend();
        effects.render(root, markdown, key, 2);
        if (!animationEndsBeforeCompletion) batch.listeners.animationend();
        assert.equal(root.textContent, sourceText, 'selection keeps live text in place until deselected');
        effects.render(root, markdown, key, 3);
        selection.isCollapsed = true;
        selection.anchorNode = null;
        selection.focusNode = null;
        selectionChanged();
        assert.equal(root.children.length, 1);
        assert.equal(root.children[0].text, markdown, 'Markdown replaces the selected raw stream after deselection');
        assert.equal(root.dataset.chronicleRenderMode, 'static');
        const completed = root.children[0];
        selectionChanged();
        assert.equal(root.children[0], completed, 'a second selection change cannot render or duplicate the response again');
        effects.render(root, markdown, key, 3);
        assert.equal(root.children[0], completed, 'a repeated final callback at the same revision leaves completed Markdown mounted');
    }
});

test('disposal clears selected Markdown handoff and ignores callbacks to the old Chronicle', () => {
    const { effects, root, flush, pendingFrame, selection, selectionListeners, selectionChanged } = setup();
    root.id = 'old-chronicle-instance';
    const sourceText = '**Selected narration.**';
    const markdown = '<p><strong>Selected narration.</strong></p>';
    effects.sync(root, 'old-segment', 1, sourceText, true, 'focus', []);
    flush();
    const batch = root.children[0];
    selection.isCollapsed = false;
    selection.anchorNode = batch;
    selection.focusNode = batch;
    batch.listeners.animationend();
    effects.render(root, markdown, 'old-segment', 2);
    assert.equal(selectionListeners().length, 1, 'one selection listener awaits final Markdown');
    const staleSelectionHandler = selectionListeners()[0];
    const oldContent = root.textContent;

    root.connected = false;
    effects.dispose(root.id);
    effects.dispose(root.id);
    assert.equal(selectionListeners().length, 0, 'repeated disposal removes the listener exactly once');

    const newRoot = new Node('div');
    newRoot.id = 'new-chronicle-instance';
    effects.sync(newRoot, 'new-segment', 1, 'New Session.', false, 'focus', []);
    flush();
    effects.render(newRoot, '<p>New Session.</p>', 'new-segment', 2);
    const newContent = newRoot.textContent;
    selection.isCollapsed = true;
    selection.anchorNode = null;
    selection.focusNode = null;
    staleSelectionHandler();
    batch.listeners.animationend();
    selectionChanged();
    effects.sync(root, 'old-segment', 3, 'late old text', true, 'focus', []);
    effects.render(root, '<p>late old text</p>', 'old-segment', 4);
    flush();
    assert.equal(root.textContent, oldContent, 'queued callbacks and late interop cannot update the disposed root');
    assert.equal(newRoot.textContent, newContent, 'the newly mounted Chronicle remains untouched');

    const pendingRoot = new Node('div');
    pendingRoot.id = 'pending-chronicle-instance';
    effects.sync(pendingRoot, 'pending-segment', 1, 'Queued text', true, 'focus', []);
    const staleFrame = pendingFrame();
    assert.equal(typeof staleFrame, 'function', 'an animation frame is queued before disposal');
    pendingRoot.connected = false;
    effects.dispose(pendingRoot.id);
    staleFrame();
    flush();
    assert.equal(pendingRoot.textContent, '', 'even an already queued frame does nothing after disposal');
});
