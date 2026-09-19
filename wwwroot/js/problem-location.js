(() => {
    const holder = document.getElementById('problem-location-map');
    const button = document.getElementById('show-problem-map');
    if (!holder || !button) return;
    const status = document.getElementById('problem-map-status');
    let map;
    button.addEventListener('click', async () => {
        if (map) {
            holder.hidden = !holder.hidden;
            if (!holder.hidden) map.invalidateSize();
            return;
        }
        button.disabled = true;
        try {
            await window.fixPalLeaflet();
            const point = [Number(holder.dataset.latitude), Number(holder.dataset.longitude)];
            holder.hidden = false;
            map = L.map(holder, { scrollWheelZoom: false }).setView(point, 15);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19, updateWhenIdle: true, keepBuffer: 1,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap contributors</a>'
            }).addTo(map).on('tileerror', () => {
                status.textContent = 'تعذر تحميل بعض الخرائط. يمكنك استخدام رابط الاتجاهات.';
            });
            L.marker(point).addTo(map).bindPopup('موقع المشكلة');
            status.textContent = 'موقع المشكلة المحدد عند إنشاء الطلب.';
        } catch {
            if (map) { map.remove(); map = null; }
            holder.hidden = true;
            status.textContent = 'تعذر تحميل الخريطة. يمكنك استخدام رابط الاتجاهات أو المدينة والمنطقة.';
        } finally { button.disabled = false; }
    });
})();
