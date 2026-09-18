(() => {
    'use strict';

    const roots = document.querySelectorAll('[data-dalil-widget]');
    if (!roots.length) return;

    const instances = [];

    roots.forEach((root) => {
        const form = root.querySelector('[data-dalil-form]');
        const input = root.querySelector('[data-dalil-input]');
        const send = root.querySelector('[data-dalil-send]');
        const messagesNode = root.querySelector('[data-dalil-messages]');
        const status = root.querySelector('[data-dalil-status]');
        const typing = root.querySelector('[data-dalil-typing]');
        const token = form?.querySelector('input[name="__RequestVerificationToken"]');
        const endpoint = root.dataset.endpoint;
        const conversation = [];
        let busy = false;
        let previouslyFocused = null;

        if (!form || !input || !send || !messagesNode || !status || !typing || !endpoint) return;

        const scrollToLatest = () => { messagesNode.scrollTop = messagesNode.scrollHeight; };

        const addDetail = (container, label, value) => {
            if (!value) return;
            const line = document.createElement('span');
            const heading = document.createElement('strong');
            heading.textContent = `${label}: `;
            line.append(heading, document.createTextNode(String(value)));
            container.append(line);
        };

        const addProviders = (container, providers) => {
            if (!Array.isArray(providers) || !providers.length) return;
            const section = document.createElement('section');
            section.className = 'dalil-assistant__providers';
            const title = document.createElement('strong');
            title.className = 'dalil-assistant__providers-title';
            title.textContent = 'مزودون مطابقون من صلّحلي';
            section.append(title);

            if (providers[0]?.matchScope === 'SameCity') {
                const fallback = document.createElement('p');
                fallback.className = 'dalil-assistant__providers-note';
                fallback.textContent = 'لم نجد مزودًا مطابقًا داخل منطقتك، وهذه خيارات من نفس المدينة.';
                section.append(fallback);
            }

            providers.forEach((provider) => {
                const card = document.createElement('article');
                card.className = 'dalil-assistant__provider';
                const name = document.createElement('strong');
                name.textContent = String(provider.displayName || 'مزود خدمة');
                const specialty = document.createElement('span');
                specialty.textContent = String(provider.specialty || '');
                const location = document.createElement('span');
                location.textContent = [provider.area, provider.city].filter(Boolean).join(' — ');
                const scope = document.createElement('span');
                scope.className = 'dalil-assistant__provider-scope';
                scope.textContent = provider.matchScope === 'SameCity'
                    ? 'خيار من نفس المدينة'
                    : 'ضمن منطقتك';
                const availability = document.createElement('span');
                availability.textContent = String(provider.availabilityLabel || '');
                const rating = document.createElement('span');
                rating.className = 'dalil-assistant__provider-rating';
                rating.textContent = provider.averageRating == null
                    ? 'لا توجد تقييمات بعد'
                    : `★ ${Number(provider.averageRating).toFixed(1)} / 5`;
                const reviews = document.createElement('span');
                reviews.textContent = provider.averageRating == null
                    ? ''
                    : `${Number(provider.reviewCount) || 0} تقييم`;
                card.append(name, specialty, location, scope, availability, rating, reviews);

                const actions = document.createElement('div');
                actions.className = 'dalil-assistant__provider-actions';

                if (typeof provider.profileUrl === 'string'
                    && provider.profileUrl.startsWith('/Providers/Details/')) {
                    const link = document.createElement('a');
                    link.href = provider.profileUrl;
                    link.textContent = 'عرض الملف الشخصي';
                    actions.append(link);
                }
                if (typeof provider.requestUrl === 'string'
                    && provider.requestUrl.startsWith('/MaintenanceRequests/Create?')) {
                    const requestLink = document.createElement('a');
                    requestLink.href = provider.requestUrl;
                    requestLink.className = 'dalil-assistant__provider-request';
                    requestLink.textContent = 'تقديم طلب صيانة';
                    actions.append(requestLink);
                }
                if (actions.childElementCount) card.append(actions);
                section.append(card);
            });
            container.append(section);
        };

        const addMessage = (role, text, response) => {
            const row = document.createElement('div');
            row.className = `dalil-assistant__message dalil-assistant__message--${role}`;
            const bubble = document.createElement('div');
            bubble.className = 'dalil-assistant__bubble';
            bubble.textContent = text;

            if (role === 'assistant' && response) {
                const details = document.createElement('div');
                details.className = 'dalil-assistant__details';
                addDetail(details, 'التخصص المقترح', response.suggestedCategory);
                addDetail(details, 'الموقع', [response.suggestedArea, response.suggestedCity]
                    .filter(Boolean).join(' — '));
                addDetail(details, 'الخطوة التالية', response.nextStep);
                if (response.locationRequired) {
                    addDetail(details, 'الموقع مطلوب', 'اكتب مدينتك ومنطقتك لعرض مزودين مطابقين.');
                }
                if (details.childElementCount) bubble.append(details);
                addProviders(bubble, response.providers);
            }

            row.append(bubble);
            messagesNode.append(row);
            scrollToLatest();
        };

        const setBusy = (value) => {
            busy = value;
            input.disabled = value;
            send.disabled = value;
            typing.hidden = !value;
            send.setAttribute('aria-busy', String(value));
            if (value) scrollToLatest();
        };

        const compactConversation = () => {
            while (conversation.length > 11) conversation.shift();
            let total = conversation.reduce((sum, message) => sum + message.content.length, 0);
            while (total > 4000 && conversation.length > 1) {
                total -= conversation[0].content.length;
                conversation.shift();
            }
        };

        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            const text = input.value.trim();
            if (busy || !text) return;

            status.textContent = '';
            addMessage('user', text);
            conversation.push({ role: 'user', content: text });
            compactConversation();
            input.value = '';
            input.style.height = '';
            setBusy(true);

            try {
                const headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
                if (token?.value) headers.RequestVerificationToken = token.value;
                const response = await fetch(endpoint, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers,
                    body: JSON.stringify({ messages: conversation })
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.success || !payload.reply) {
                    status.textContent = payload?.reply || 'دليل غير متاح مؤقتًا. حاول مرة أخرى بعد قليل.';
                    return;
                }
                addMessage('assistant', payload.reply, payload);
                if (typeof payload.historyReply === 'string' && payload.historyReply.trim()) {
                    conversation.push({ role: 'assistant', content: payload.historyReply.trim() });
                }
                compactConversation();
            } catch {
                status.textContent = 'تعذر الاتصال بدليل. تحقق من الاتصال وحاول مرة أخرى.';
            } finally {
                setBusy(false);
                input.focus();
            }
        });

        input.addEventListener('keydown', (event) => {
            if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
                event.preventDefault();
                form.requestSubmit();
            }
        });
        input.addEventListener('input', () => {
            input.style.height = 'auto';
            input.style.height = `${Math.min(input.scrollHeight, 132)}px`;
        });

        const open = () => {
            previouslyFocused = document.activeElement;
            root.hidden = false;
            document.body.style.overflow = 'hidden';
            input.focus();
        };
        const close = () => {
            root.hidden = true;
            document.body.style.overflow = '';
            if (previouslyFocused instanceof HTMLElement) previouslyFocused.focus();
        };
        root.querySelectorAll('[data-dalil-close]').forEach((button) => button.addEventListener('click', close));
        root.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                close();
                return;
            }
            if (event.key !== 'Tab') return;
            const focusable = [...root.querySelectorAll('button:not([disabled]), textarea:not([disabled]), input:not([disabled])')]
                .filter((element) => element instanceof HTMLElement && element.offsetParent !== null);
            if (!focusable.length) return;
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        });
        instances.push({ open, close });
    });

    window.DalilAssistant = {
        open: () => instances[0]?.open(),
        close: () => instances[0]?.close()
    };
})();
