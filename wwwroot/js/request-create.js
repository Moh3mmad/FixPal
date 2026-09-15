(() => {
    const form = document.getElementById('request-form');
    if (!form) return;
    const city = document.getElementById('CityId'), area = document.getElementById('AreaId');
    const category = document.getElementById('ServiceCategoryId'), provider = document.getElementById('ProviderProfileId');
    const type = document.getElementById('RequestType'), search = document.getElementById('provider-search');
    const status = document.getElementById('provider-search-status');
    const matches = document.getElementById('provider-matches');
    const areaOptions = Array.from(area.options).filter(o => o.value).map(o => ({ value: o.value, text: o.text, city: o.dataset.city }));
    let activeSearch;
    function filterAreas() {
        const selected = area.value;
        area.replaceChildren(new Option('اختر المنطقة', ''));
        areaOptions.filter(o => o.city === city.value).forEach(o => area.add(new Option(o.text, o.value)));
        area.value = areaOptions.some(o => o.city === city.value && o.value === selected) ? selected : '';
    }
    async function findProviders() {
        activeSearch?.abort();
        activeSearch = new AbortController();
        provider.replaceChildren(new Option('بدون تعيين مزود الآن', ''));
        matches.replaceChildren();
        if (type.value === '2' || !category.value || !area.value) { status.textContent = 'اختر التخصص والمدينة والمنطقة أولًا.'; return; }
        status.textContent = 'جارٍ البحث…';
        const url = new URL(form.dataset.providersUrl, location.origin);
        url.search = new URLSearchParams({ categoryId: category.value, areaId: area.value, q: search.value });
        try {
            const response = await fetch(url, { signal: activeSearch.signal, headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Search failed');
            const providers = await response.json();
            providers.forEach(p => {
                provider.add(new Option(p.displayName, p.id));
                const card = document.createElement('div'); card.className = 'fp-match-card';
                const title = document.createElement('strong'); title.textContent = p.displayName;
                const reason = document.createElement('p'); reason.textContent = p.reason;
                const choose = document.createElement('button'); choose.type = 'button'; choose.className = 'btn fp-btn-ghost'; choose.textContent = 'اختيار هذا المزود';
                choose.addEventListener('click', () => { provider.value = String(p.id); status.textContent = `تم اختيار ${p.displayName}. راجع طلبك قبل الإرسال.`; provider.focus(); });
                card.append(title, reason, choose); matches.append(card);
            });
            status.textContent = providers.length ? `${providers.length} اقتراحات ضمن التخصص والمنطقة، مرتبة بالتوفر ثم التقييمات. لا تمثل ترتيبًا بالمسافة.` : 'لا يوجد مزود مطابق للمنطقة والتخصص. يمكنك تسجيل الطلب بدون تعيين.';
        } catch (error) { if (error.name !== 'AbortError') status.textContent = 'تعذر تحميل المزودين. أعد البحث أو أرسل الطلب بدون تعيين.'; }
    }
    function setType() {
        const report = type.value === '2';
        document.getElementById('public-report-note').hidden = !report;
        document.getElementById('provider-selection').hidden = report;
        if (report) { activeSearch?.abort(); provider.value = ''; }
    }
    city.addEventListener('change', () => { filterAreas(); findProviders(); });
    area.addEventListener('change', findProviders);
    category.addEventListener('change', findProviders);
    type.addEventListener('change', () => { setType(); findProviders(); });
    document.getElementById('find-providers').addEventListener('click', findProviders);
    search.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); findProviders(); } });
    filterAreas(); setType();
    if (!provider.value && category.value && area.value) findProviders();
})();
