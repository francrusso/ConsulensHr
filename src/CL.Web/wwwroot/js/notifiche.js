// Impostazioni notifiche: ogni interruttore di email e WhatsApp si salva subito (POST /Notifiche/Scelta).
// "Disattiva tutte" di una sezione e l'interruttore generale di un canale agiscono su più tipologie insieme.
// Gli interruttori disattivati (WhatsApp senza numero collegato) non si toccano. Se il salvataggio non riesce
// tornano com'erano e compare il messaggio d'errore.
(function () {
    const pagina = document.querySelector('.impostazioni-notifiche');
    if (!pagina) return;

    const url = pagina.dataset.salva;
    const token = pagina.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const stato = pagina.querySelector('.stato-salvataggio');
    const canali = ['email', 'whatsapp'];
    const delCanale = canale => Array.from(pagina.querySelectorAll(`.chk-canale[data-canale="${canale}"]:not(:disabled)`));
    const dellaSezione = sezione => Array.from(sezione.querySelectorAll('.chk-canale:not(:disabled)'));
    let timerStato;

    function mostra(testo, errore) {
        if (!stato) return;
        stato.textContent = testo;
        stato.classList.toggle('errore', !!errore);
        clearTimeout(timerStato);
        if (!errore) timerStato = setTimeout(() => { stato.textContent = ''; }, 2500);
    }

    function aggiorna() {
        canali.forEach(canale => {
            const caselle = delCanale(canale);
            if (caselle.length === 0) return;
            const attive = caselle.filter(c => c.checked).length;
            const card = pagina.querySelector(`.card-canale[data-canale="${canale}"]`);
            const generale = card?.querySelector('.chk-canale-tutte');
            if (generale) generale.checked = attive > 0;
            card?.classList.toggle('attivo', attive > 0);
            const conteggio = card?.querySelector('.conteggio-canale');
            if (conteggio) conteggio.textContent = `${attive} di ${caselle.length} attive`;
        });
        pagina.querySelectorAll('.card-categoria-notifica').forEach(sezione => {
            const pulsante = sezione.querySelector('.toggle-categoria');
            if (pulsante) pulsante.textContent = dellaSezione(sezione).some(c => c.checked) ? 'Disattiva tutte' : 'Attiva tutte';
        });
    }

    // Una richiesta per canale: le caselle di email e WhatsApp di una sezione partono insieme.
    async function salva(caselle, attiva) {
        const precedenti = caselle.map(c => c.checked);
        caselle.forEach(c => { c.checked = attiva; });
        aggiorna();
        try {
            for (const canale of canali) {
                const delGruppo = caselle.filter(c => c.dataset.canale === canale);
                if (delGruppo.length === 0) continue;
                const corpo = new URLSearchParams({ canale: canale === 'whatsapp' ? 'WhatsApp' : 'Email', attiva: attiva, __RequestVerificationToken: token });
                delGruppo.forEach(c => corpo.append('tipi', c.value));
                const risposta = await fetch(url, { method: 'POST', credentials: 'same-origin', body: corpo });
                if (!risposta.ok) throw new Error(risposta.status);
            }
            mostra(attiva ? 'Notifiche attivate' : 'Notifiche disattivate');
        } catch {
            caselle.forEach((c, i) => { c.checked = precedenti[i]; });
            aggiorna();
            mostra('Impossibile salvare: ricarica la pagina e riprova.', true);
        }
    }

    pagina.querySelectorAll('.chk-canale').forEach(c => c.addEventListener('change', () => {
        const attiva = c.checked;
        c.checked = !attiva; // salva() lo imposta e, se non riesce, lo rimette
        salva([c], attiva);
    }));

    pagina.querySelectorAll('.card-categoria-notifica .toggle-categoria').forEach(pulsante => pulsante.addEventListener('click', () => {
        const caselle = dellaSezione(pulsante.closest('.card-categoria-notifica'));
        salva(caselle, !caselle.some(c => c.checked));
    }));

    pagina.querySelectorAll('.chk-canale-tutte').forEach(generale => generale.addEventListener('change', () => {
        salva(delCanale(generale.dataset.canale), generale.checked);
    }));
})();
