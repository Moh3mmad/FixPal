(() => {
    const panel = document.getElementById('diagnosis-panel');
    if (!panel) return;
    const button = document.getElementById('diagnose'), output = document.getElementById('diagnosis-result');
    const apply = document.getElementById('apply-diagnosis'), category = document.getElementById('ServiceCategoryId');
    let suggested = null;
    button.addEventListener('click', async () => {
        apply.hidden = true; suggested = null;
        if (!document.getElementById('diagnosis-consent').checked) { output.textContent = 'وافق على إرسال البيانات للمساعدة الذكية، أو تابع الطلب يدويًا.'; return; }
        const description = document.getElementById('Description').value.trim();
        if (description.length < 10 || description.length > 2000) { output.textContent = 'اكتب وصفًا من 10 إلى 2000 حرف أولًا.'; return; }
        const file = document.getElementById('diagnosis-image').files[0];
        if (file && file.size > 5 * 1024 * 1024) { output.textContent = 'الصورة أكبر من 5 ميغابايت.'; return; }
        const body = new FormData();
        body.append('Description', description); body.append('Consent', 'true');
        body.append('__RequestVerificationToken', document.querySelector('#request-form input[name="__RequestVerificationToken"]').value);
        if (file) body.append('Image', file);
        button.disabled = true; output.textContent = 'جارٍ مراجعة الوصف… يمكنك متابعة ملء الطلب.';
        const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 18000);
        try {
            const response = await fetch(panel.dataset.url, { method: 'POST', body, signal: controller.signal });
            if (response.status === 429) throw new Error('انتظر دقيقة قبل طلب اقتراح جديد. يمكنك المتابعة يدويًا.');
            if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('تعذرت المساعدة الذكية. تابع الطلب يدويًا.');
            const data = await response.json();
            output.replaceChildren();
            function line(text) { const p = document.createElement('p'); p.textContent = text; output.append(p); }
            line(data.message);
            if (data.result) {
                line(data.result.shortDiagnosis);
                const severity = { low: 'منخفضة', medium: 'متوسطة', high: 'مرتفعة', emergency: 'طارئة' };
                line(`درجة الأولوية: ${severity[data.result.severity] || 'غير محددة'}${data.result.requiresProfessional ? ' — يحتاج مختصًا' : ''}`);
                line(data.result.suggestedNextStep);
                if (data.result.confidence != null) line(`ثقة تقديرية من النموذج: ${Math.round(data.result.confidence * 100)}٪، وليست ضمانًا.`);
                suggested = Array.from(category.options).find(o => o.value === String(data.result.suggestedCategory));
                if (suggested) { line(`التخصص المقترح: ${suggested.text}`); apply.hidden = false; }
            }
        } catch (error) { output.textContent = error.name === 'AbortError' ? 'انتهت مهلة المساعدة. يمكنك إرسال الطلب يدويًا.' : error.message; }
        finally { clearTimeout(timer); button.disabled = false; }
    });
    apply.addEventListener('click', () => { if (suggested) { category.value = suggested.value; category.dispatchEvent(new Event('change')); apply.hidden = true; category.focus(); } });
})();
