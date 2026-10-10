import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
const source = readFileSync(new URL('../../src/DBLC/wwwroot/app.js', import.meta.url), 'utf8');
const start = source.indexOf('window.diceControls = (() => {');
const end = source.indexOf('})();', start) + 4;
class Element {
    constructor(type, parent = null) { this.type = type; this.parent = parent; this.dataset = {}; this.listeners = {}; this.classes = new Set(); this.hidden = false; this.disabled = false; this.classList = { add: x => this.classes.add(x), remove: x => this.classes.delete(x) }; }
    addEventListener(name, handler, options) { (this.listeners[name] ??= []).push(handler); options?.signal?.addEventListener('abort', () => { this.listeners[name] = this.listeners[name].filter(x => x !== handler); }); }
    emit(name, values = {}) { const event = { target: this, button: 0, pointerId: 1, clientX: 0, clientY: 0, detail: 1, preventDefault() {}, ...values }; for (const handler of this.listeners[name] || []) handler(event); }
    contains(node) { return node === this || !!node?.parent && this.contains(node.parent); }
    closest(selector) { return selector === '.die-choice' && this.type === 'choice' ? this : this.parent?.closest(selector); }
    setAttribute(name, value) { this[name] = value; }
    focus() { this.focused = true; }
}
function setup(compact = false) {
    const root = new Element('root'); root.dataset = { compact: String(compact), session: 'session-1' };
    const button = new Element('button', root), menu = new Element('menu', root), doc = new Element('document'); menu.hidden = true;
    const choices = [4, 6, 8, 10, 12, 20].map(sides => { const choice = new Element('choice', menu); choice.dataset.sides = String(sides); return choice; });
    let selected = choices.at(-1), underPointer = button, now = 0, timerId = 0;
    const timers = new Map(), calls = [];
    root.querySelector = selector => selector === '.die-roll-button' ? button : selector === '.die-picker' ? menu : selected;
    menu.querySelector = () => selected;
    doc.elementFromPoint = () => underPointer;
    const context = { window: {}, document: doc, AbortController, crypto: { randomUUID: () => `event-${calls.length + 1}` }, setTimeout: (fn, ms) => { const id = ++timerId; timers.set(id, { fn, at: now + ms }); return id; }, clearTimeout: id => timers.delete(id) };
    vm.runInNewContext(source.slice(start, end), context);
    context.window.diceControls.install(root, { invokeMethodAsync: async (method, sides, roll, id, session) => { calls.push({ method, sides, roll, id, session }); selected = choices.find(choice => +choice.dataset.sides === sides); } });
    const advance = ms => { now += ms; for (const [id, timer] of [...timers]) if (timer.at <= now) { timers.delete(id); timer.fn(); } };
    const settle = async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); };
    return { root, button, menu, doc, choices, calls, advance, settle, pointAt: el => underPointer = el, dispose: () => context.window.diceControls.dispose(root) };
}
test('desktop hold-hover-release rolls chosen die exactly once', async () => {
    const s = setup(); s.button.emit('pointerdown'); s.advance(720); assert.equal(s.menu.hidden, false);
    s.pointAt(s.choices[1]); s.doc.emit('pointerup', { target: s.choices[1] }); s.menu.emit('click', { target: s.choices[1] }); await s.settle();
    assert.equal(s.calls.length, 1); assert.equal(s.calls[0].sides, 6); assert.equal(s.calls[0].roll, true); assert.equal(s.menu.hidden, true);
});
test('desktop hold-release-click lets the player preselect without rolling; outside and Escape never roll', async () => {
    const s = setup(); s.button.emit('pointerdown'); s.advance(720); s.pointAt(s.button); s.doc.emit('pointerup', { target: s.button }); s.button.emit('click');
    assert.equal(s.calls.length, 0); assert.equal(s.menu.hidden, false);
    s.menu.emit('click', { target: s.choices[0] }); await s.settle(); assert.equal(s.calls.length, 1); assert.equal(s.calls[0].sides, 4); assert.equal(s.calls[0].roll, false); assert.equal(s.menu.hidden, true);
    s.button.emit('pointerdown'); s.doc.emit('pointerup', { target: s.button }); s.button.emit('click'); await s.settle(); assert.equal(s.calls.length, 2); assert.equal(s.calls[1].sides, 4); assert.equal(s.calls[1].roll, true);
    s.button.emit('pointerdown'); s.advance(720); s.doc.emit('pointerup'); s.doc.emit('keydown', { key: 'Escape' }); assert.equal(s.menu.hidden, true);
    s.button.emit('pointerdown'); s.advance(720); s.doc.emit('pointerup'); s.doc.emit('pointerdown', { target: new Element('outside') }); assert.equal(s.menu.hidden, true); assert.equal(s.calls.length, 2);
});
test('mobile tap rolls; long press and selection do not roll; subsequent tap uses selected die', async () => {
    const s = setup(true); s.button.emit('pointerdown'); s.doc.emit('pointerup', { target: s.button }); s.button.emit('click'); await s.settle();
    assert.equal(s.calls.length, 1); assert.equal(s.calls[0].sides, 20);
    for (const choice of s.choices) {
        s.button.emit('pointerdown'); s.advance(720); s.pointAt(choice); s.doc.emit('pointerup', { target: s.button }); s.button.emit('click'); assert.equal(s.menu.hidden, false);
        const before = s.calls.length; s.menu.emit('click', { target: choice }); await s.settle(); assert.equal(s.calls.length, before + 1); assert.equal(s.calls.at(-1).roll, false);
        s.button.emit('pointerdown'); s.doc.emit('pointerup', { target: s.button }); s.button.emit('click'); await s.settle(); assert.equal(s.calls.at(-1).roll, true); assert.equal(s.calls.at(-1).sides, +choice.dataset.sides);
    }
});
test('cancelled, disabled, stale-session, repeated and disposed gestures cannot accidentally roll', async () => {
    const s = setup(); s.button.disabled = true; s.button.emit('pointerdown'); s.doc.emit('pointerup', { target: s.button }); assert.equal(s.calls.length, 0);
    s.button.disabled = false; s.button.emit('pointerdown'); s.doc.emit('pointercancel'); s.doc.emit('pointerup', { target: s.button }); assert.equal(s.calls.length, 0);
    s.button.emit('pointerdown'); s.root.dataset.session = 'session-2'; s.doc.emit('pointerup', { target: s.button }); assert.equal(s.calls.length, 0);
    s.button.emit('pointerdown'); s.doc.emit('pointerup', { target: s.button }); s.doc.emit('pointerup', { target: s.button }); await s.settle(); assert.equal(s.calls.length, 1);
    s.dispose(); s.button.emit('pointerdown'); s.advance(720); s.doc.emit('pointerup', { target: s.button }); assert.equal(s.calls.length, 1);
});
