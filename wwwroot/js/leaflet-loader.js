(() => {
    let loading;
    window.fixPalLeaflet = function () {
        if (window.L) return Promise.resolve();
        if (loading) return loading;
        loading = new Promise((resolve, reject) => {
            const css = document.createElement('link'); css.rel = 'stylesheet';
            css.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
            css.integrity = 'sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY='; css.crossOrigin = '';
            const script = document.createElement('script'); script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
            script.integrity = 'sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo='; script.crossOrigin = '';
            let cssReady = false, jsReady = false;
            const timer = setTimeout(() => reject(new Error('Map timeout')), 10000);
            const done = () => { if (cssReady && jsReady) { clearTimeout(timer); resolve(); } };
            css.onload = () => { cssReady = true; done(); }; script.onload = () => { jsReady = true; done(); };
            css.onerror = script.onerror = () => { clearTimeout(timer); reject(new Error('Map unavailable')); };
            document.head.append(css); document.head.append(script);
        });
        return loading;
    };
})();
