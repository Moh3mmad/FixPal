const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');

// Exercise the production event handlers without external maps or real GPS.
function setup({ geolocation, failMap = false, details = false } = {}) {
    const elements = {};
    const ids = details
        ? ['problem-location-map', 'show-problem-map', 'problem-map-status']
        : ['Latitude', 'Longitude', 'location-status', 'request-map', 'clear-location', 'use-location', 'open-map'];
    for (const id of ids) elements[id] = {
        value: '', hidden: true, disabled: false, dataset: { latitude: '31.81234', longitude: '35.23456' },
        addEventListener(event, handler) { this[event] = handler; }
    };
    const map = {
        setView(point) { this.point = point; return this; },
        on(event, handler) { this[event] = handler; return this; }, invalidateSize() {}, remove() {}
    };
    const marker = { addTo() { return this; }, setLatLng() {}, remove() {}, bindPopup() { return this; } };
    const context = {
        document: { getElementById: id => elements[id] }, navigator: { geolocation },
        window: { fixPalLeaflet: async () => { if (failMap) throw Error('offline'); } },
        L: { map: () => map, marker: point => { marker.point = point; return marker; },
            tileLayer: () => ({ addTo() { return this; }, on() { return this; } }) }
    };
    vm.runInNewContext(readFileSync(join(__dirname, '../wwwroot/js/', details ? 'problem-location.js' : 'request-location.js'), 'utf8'), context);
    return { elements, map, marker };
}

test('GPS success populates optional fields; clear restores City/Area fallback', () => {
    const { elements: e } = setup({ geolocation: { getCurrentPosition(ok) { ok({ coords: { latitude: 31.81234, longitude: 35.23456 } }); } } });
    e['use-location'].click();
    assert.equal(e.Latitude.value, '31.81234');
    assert.equal(e.Longitude.value, '35.23456');
    assert.equal(e['use-location'].disabled, false);
    e['clear-location'].click();
    assert.equal(e.Latitude.value, '');
    assert.equal(e.Longitude.value, '');
});

test('GPS denial leaves optional fields empty and permits manual map selection', async () => {
    const { elements: e, map } = setup({ geolocation: { getCurrentPosition(ok, deny) { deny(); } } });
    e['use-location'].click();
    assert.equal(e.Latitude.value, '');
    assert.equal(e['use-location'].disabled, false);
    await e['open-map'].click();
    map.click({ latlng: { lat: 31.81234, lng: 35.23456 } });
    assert.equal(e.Latitude.value, '31.81234');
    assert.equal(e.Longitude.value, '35.23456');
});

test('Unsupported GPS and map failure leave City/Area fallback available', async () => {
    const { elements: e } = setup({ failMap: true });
    e['use-location'].click();
    await e['open-map'].click();
    assert.equal(e.Latitude.value, '');
    assert.equal(e.Longitude.value, '');
    assert.equal(e['open-map'].disabled, false);
    assert.equal(e['request-map'].hidden, true);
});

test('Manual map point sets coordinates and marker', async () => {
    const { elements: e, map, marker } = setup();
    await e['open-map'].click();
    map.click({ latlng: { lat: 31.81234, lng: 395.23456 } });
    assert.equal(e.Longitude.value, '35.23456');
    assert.equal(marker.point[0], 31.81234);
});

test('Authorized detail map is lazy and displays exact marker', async () => {
    const { elements: e, marker } = setup({ details: true });
    assert.equal(marker.point, undefined);
    await e['show-problem-map'].click();
    assert.deepEqual(Array.from(marker.point), [31.81234, 35.23456]);
    assert.equal(e['problem-location-map'].hidden, false);
});

test('Detail map failure preserves page and gives fallback', async () => {
    const { elements: e } = setup({ details: true, failMap: true });
    await e['show-problem-map'].click();
    assert.equal(e['problem-location-map'].hidden, true);
    assert.equal(e['show-problem-map'].disabled, false);
    assert.match(e['problem-map-status'].textContent, /الاتجاهات/);
});

test('Restricted detail page has no map behavior or external requests', () => {
    vm.runInNewContext(readFileSync(join(__dirname, '../wwwroot/js/problem-location.js'), 'utf8'), {
        document: { getElementById: () => null }
    });
});
