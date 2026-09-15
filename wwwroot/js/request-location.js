(() => {
    const latitude = document.getElementById('Latitude'), longitude = document.getElementById('Longitude');
    if (!latitude || !longitude) return;
    const status = document.getElementById('location-status'), holder = document.getElementById('request-map');
    let map, marker, loading;
    function setPoint(lat, lng) {
        latitude.value = lat.toFixed(5); longitude.value = lng.toFixed(5);
        status.textContent = `تم تحديد نقطة خاصة (${latitude.value}, ${longitude.value}). راجع المدينة والمنطقة قبل الإرسال.`;
        if (map) {
            if (marker) marker.setLatLng([lat, lng]);
            else marker = L.marker([lat, lng]).addTo(map);
            map.setView([lat, lng], 15);
        }
    }
    if (latitude.value && longitude.value) setPoint(Number(latitude.value), Number(longitude.value));
    document.getElementById('clear-location').addEventListener('click', () => {
        latitude.value = ''; longitude.value = '';
        if (marker) { marker.remove(); marker = null; }
        status.textContent = 'تم مسح النقطة. سيُستخدم اختيار المدينة والمنطقة.';
    });
    const gps = document.getElementById('use-location');
    gps.addEventListener('click', () => {
        if (!navigator.geolocation) { status.textContent = 'المتصفح لا يدعم تحديد الموقع. اختر المدينة والمنطقة.'; return; }
        gps.disabled = true; status.textContent = 'بانتظار إذن الموقع…';
        navigator.geolocation.getCurrentPosition(position => {
            setPoint(position.coords.latitude, position.coords.longitude); gps.disabled = false;
        }, () => { gps.disabled = false; status.textContent = 'لم نحصل على الموقع. اختر نقطة على الخريطة أو تابع يدويًا.'; },
        { enableHighAccuracy: false, timeout: 10000, maximumAge: 60000 });
    });
    function leaflet() {
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
    }
    const open = document.getElementById('open-map');
    open.addEventListener('click', async () => {
        if (map) { holder.hidden = !holder.hidden; if (!holder.hidden) map.invalidateSize(); return; }
        open.disabled = true; status.textContent = 'جارٍ تحميل الخريطة…';
        try {
            await leaflet(); holder.hidden = false;
            // Jerusalem is the initial demo viewport only; the map can be panned worldwide.
            map = L.map(holder, { scrollWheelZoom: false }).setView([31.77, 35.22], 12);
            const tiles = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, updateWhenIdle: true, keepBuffer: 1,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap contributors</a>' }).addTo(map);
            tiles.on('tileerror', () => { status.textContent = 'تعذر تحميل بعض الخرائط. يمكنك المتابعة بالمدينة والمنطقة.'; });
            map.on('click', event => setPoint(event.latlng.lat, ((event.latlng.lng + 180) % 360 + 360) % 360 - 180));
            status.textContent = 'اضغط على موقع المشكلة لتحديد نقطة. لا يغيّر ذلك المدينة والمنطقة تلقائيًا.';
            if (latitude.value && longitude.value) setPoint(Number(latitude.value), Number(longitude.value));
        } catch { holder.hidden = true; status.textContent = 'الخريطة غير متاحة. يمكنك تحديد GPS أو إرسال الطلب بالمدينة والمنطقة.'; }
        finally { open.disabled = false; }
    });
})();
