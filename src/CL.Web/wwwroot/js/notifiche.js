// Impostazioni notifiche: ogni interruttore dell'email si salva subito (POST /Notifiche/Email).
// "Disattiva tutte" di una sezione e l'interruttore del canale Email agiscono su più tipologie insieme.
// Se il salvataggio non riesce gli interruttori tornano com'erano e compare il messaggio d'errore.
(function () {
    const pagina = document.querySelector('.impostazioni-notifiche');
    if (!pagina) return;

    const url = pagina.dataset.salva;
    const token = pagina.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const stato = pagina.querySelector('.stato-salvataggio');
    const canaleEmail = pagina.querySelector('.chk-canale-email');
    const cardEmail = pagina.querySelector('[data-canale="email"]');
    const conteggio = pagina.querySelector('#conteggio-email');
    const tutti = () => Array.from(pagina.querySelectorAll('.chk-email'));
    let timerStato;

    function mostra(testo, errore) {
        if (!stato) return;
        stato.textContent = testo;
        stato.classList.toggle('errore', !!errore);
        clearTimeout(timerStato);
        if (!errore) timerStato = setTimeout(() => { stato.textContent = ''; }, 2500);
    }

    function aggiorna() {
        const attivi = tutti().filter(c => c.checked).length;
        if (canaleEmail) canaleEmail.checked = attivi > 0;
        cardEmail?.classList.toggle('attivo', attivi > 0);
        if (conteggio) conteggio.textContent = `${attivi} di ${conteggio.dataset.totale} attive`;
        pagina.querySelectorAll('.card-categoria-notifica').forEach(sezione => {
            const pulsante = sezione.querySelector('.toggle-categoria');
            if (pulsante) pulsante.textContent = Array.from(sezione.querySelectorAll('.chk-email')).some(c => c.checked) ? 'Disattiva tutte' : 'Attiva tutte';
        });
    }

    async function salva(caselle, attiva) {
        const precedenti = caselle.map(c => c.checked);
        caselle.forEach(c => { c.checked = attiva; });
        aggiorna();
        const corpo = new URLSearchParams({ attiva: attiva, __RequestVerificationToken: token });
        caselle.forEach(c => corpo.append('tipi', c.value));
        try {
            const risposta = await fetch(url, { method: 'POST', credentials: 'same-origin', body: corpo });
            if (!risposta.ok) throw new Error(risposta.status);
            mostra(attiva ? 'Email attivate' : 'Email disattivate');
        } catch {
            caselle.forEach((c, i) => { c.checked = precedenti[i]; });
            aggiorna();
            mostra('Impossibile salvare: riprova tra qualche istante.', true);
        }
    }

    tutti().forEach(c => c.addEventListener('change', () => {
        const attiva = c.checked;
        c.checked = !attiva; // salva() lo imposta e, se non riesce, lo rimette
        salva([c], attiva);
    }));

    pagina.querySelectorAll('.card-categoria-notifica .toggle-categoria').forEach(pulsante => pulsante.addEventListener('click', () => {
        const caselle = Array.from(pulsante.closest('.card-categoria-notifica').querySelectorAll('.chk-email'));
        salva(caselle, !caselle.some(c => c.checked));
    }));

    canaleEmail?.addEventListener('change', () => {
        const attiva = canaleEmail.checked;
        salva(tutti(), attiva);
    });
})();
