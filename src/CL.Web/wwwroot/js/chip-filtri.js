// Chip dei filtri attivi, in tutte le pagine con filtri. Ogni campo di un form di ricerca (GET) applicato
// nell'indirizzo diventa un chip "Etichetta: valore" sotto la barra dei filtri: la ricerca testuale e i menù che
// hanno una voce "Tutti…" (valore vuoto). La ✕ ricarica la pagina senza quel filtro, dalla prima pagina.
// I menù senza "Tutti…" (anno, mese, vista) e i campi nascosti non sono filtri da togliere.
// Posizione: il segnaposto [data-chip-filtri] (partial _ChipRicerca), altrimenti subito dopo la barra dei filtri,
// altrimenti dopo il blocco della pagina che contiene il form.
// Sul campo: data-chip="Etichetta" sceglie l'etichetta; data-chip-togli-anche="altro" toglie anche un filtro collegato.
(function () {
    const parametri = new URLSearchParams(location.search);
    const chip = [];
    const visti = new Set();

    function etichetta(campo) {
        if (campo.dataset.chip) return campo.dataset.chip;
        if (campo.tagName !== 'SELECT') return 'Ricerca';
        const label = campo.id ? document.querySelector(`label[for="${CSS.escape(campo.id)}"]`) : null;
        const testo = (label?.textContent || campo.getAttribute('aria-label') || campo.name).trim().replace(/^filtra\s+(per\s+)?/i, '');
        return testo.charAt(0).toUpperCase() + testo.slice(1);
    }

    document.querySelectorAll('main form').forEach(form => {
        if ((form.getAttribute('method') || 'get').toLowerCase() !== 'get') return;
        // form.elements comprende anche i campi fuori dal form collegati con l'attributo form="…".
        Array.from(form.elements).filter(e => e.name && (e.tagName === 'INPUT' || e.tagName === 'SELECT')).forEach(campo => {
            const nome = campo.name;
            const valore = (parametri.get(nome) ?? '').trim();
            if (!valore || visti.has(nome) || campo.type === 'hidden') return;
            let testo;
            if (campo.tagName === 'SELECT') {
                const opzioni = Array.from(campo.options);
                if (!opzioni.some(o => o.value === '')) return;
                const scelta = opzioni.find(o => o.value.toLowerCase() === valore.toLowerCase()); // gli id possono cambiare di maiuscole
                if (!scelta) return;
                testo = scelta.text.trim();
            } else if (campo.type === 'search' || campo.type === 'text') {
                testo = `"${valore}"`;
            } else return;
            visti.add(nome);
            chip.push({
                nome, testo, campo,
                etichetta: etichetta(campo),
                anche: (campo.dataset.chipTogliAnche || '').split(/\s+/).filter(Boolean),
                icona: campo.tagName === 'SELECT' ? 'ri-filter-3-line' : 'ri-search-line',
            });
        });
    });
    if (chip.length === 0) return;

    let contenitore = document.querySelector('[data-chip-filtri]');
    if (!contenitore) {
        contenitore = document.createElement('div');
        contenitore.className = 'chip-filtri mb-3';
        const barra = document.querySelector('main form.invoice-filter-bar');
        if (barra) barra.after(contenitore);
        else {
            const pagina = document.querySelector('.page-content');
            let blocco = chip[0].campo;
            while (blocco.parentElement && blocco.parentElement !== pagina) blocco = blocco.parentElement;
            blocco.after(contenitore);
        }
    }
    contenitore.setAttribute('aria-label', 'Filtri attivi');
    contenitore.hidden = false;

    chip.forEach(c => {
        const senza = new URLSearchParams(location.search);
        [c.nome, ...c.anche, 'pagina'].forEach(n => senza.delete(n));
        const resto = senza.toString();

        const el = document.createElement('span');
        el.className = 'chip-filtro';
        const icona = document.createElement('i');
        icona.className = c.icona;
        icona.setAttribute('aria-hidden', 'true');
        const valore = document.createElement('strong');
        valore.textContent = c.testo;
        const togli = document.createElement('a');
        togli.className = 'chip-togli';
        togli.href = location.pathname + (resto ? '?' + resto : '');
        togli.title = `Togli il filtro ${c.etichetta}`;
        togli.setAttribute('aria-label', togli.title);
        togli.innerHTML = '<i class="ri-close-line" aria-hidden="true"></i>';
        el.append(icona, ` ${c.etichetta}: `, valore, togli);
        contenitore.append(el);
    });
})();
