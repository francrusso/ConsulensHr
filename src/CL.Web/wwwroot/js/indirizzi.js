// Campi di indirizzo con dati controllati:
// - via con suggerimenti (OpenStreetMap) che compilano CAP, comune e provincia: <input data-indirizzo="gruppo">
// - comune come select con ricerca sull'elenco ISTAT:                          <select data-comune="gruppo">
// - provincia come select con ricerca:                                          <select data-provincia="gruppo">
// - CAP:                                                                        <input data-cap="gruppo">
// Il "gruppo" lega i campi di uno stesso indirizzo (es. residenza e domicilio nella stessa pagina).
// Le ricerche passano dal server (IndirizziController), che tiene in memoria le risposte del servizio esterno.
(function ($) {
    const urlComuni = '/Indirizzi/Comuni', urlVie = '/Indirizzi/Cerca';
    const lingua = {
        noResults: () => 'Nessun risultato', searching: () => 'Cerco…',
        inputTooShort: () => 'Scrivi il nome del comune', errorLoading: () => 'Impossibile caricare l\'elenco',
    };
    const campo = (tipo, gruppo) => document.querySelector(`[data-${tipo}="${gruppo}"]`);

    // La select del comune contiene solo le voci già cercate: il comune scelto da un suggerimento si aggiunge come opzione.
    function impostaComune(gruppo, nome, provincia) {
        const sel = campo('comune', gruppo);
        if (sel && nome) {
            let opt = [...sel.options].find(o => o.value === nome);
            if (!opt) { opt = new Option(provincia ? `${nome} (${provincia})` : nome, nome, true, true); sel.append(opt); }
            $(sel).val(nome).trigger('change.select2');
        }
        const prov = campo('provincia', gruppo);
        if (prov && provincia) $(prov).val(provincia).trigger('change.select2');
    }

    function preparaSelect() {
        $('select[data-provincia]').each(function () {
            if ($(this).data('select2')) return;
            $(this).select2({ width: '100%', language: lingua, placeholder: 'Scegli…', allowClear: true });
        });
        // Comuni cercati sul server (circa 8.000: non si caricano tutti nella pagina); scegliendo il comune si imposta la provincia.
        $('select[data-comune]').each(function () {
            if ($(this).data('select2')) return;
            const gruppo = this.dataset.comune;
            $(this).select2({
                width: '100%', language: lingua, placeholder: 'Cerca il comune…', allowClear: true, minimumInputLength: 2,
                ajax: {
                    url: urlComuni, delay: 200, dataType: 'json',
                    // Con la provincia già scelta si cercano solo i suoi comuni.
                    data: p => ({ q: p.term, p: campo('provincia', gruppo)?.value || '' }),
                    processResults: d => ({ results: d }),
                },
            }).on('select2:select', e => {
                const prov = campo('provincia', gruppo);
                if (prov && e.params.data.provincia) $(prov).val(e.params.data.provincia).trigger('change.select2');
            });
        });
    }

    // Campo via con l'elenco dei suggerimenti sotto (stile dropdown di Bootstrap), navigabile anche da tastiera.
    function preparaVia(input) {
        if (input.dataset.pronto) return;
        input.dataset.pronto = '1';
        const gruppo = input.dataset.indirizzo;
        const contenitore = document.createElement('div');
        contenitore.className = 'position-relative';
        input.replaceWith(contenitore);
        contenitore.append(input);
        const lista = document.createElement('ul');
        lista.className = 'dropdown-menu suggerimenti-indirizzo w-100';
        lista.setAttribute('role', 'listbox');
        lista.id = 'suggerimenti-' + gruppo;
        contenitore.append(lista);
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-controls', lista.id);
        input.setAttribute('aria-expanded', 'false');
        if (!input.placeholder) input.placeholder = 'Inizia a scrivere: via e numero civico';

        let timer, risultati = [], attivo = -1, ultima = '';
        const chiudi = () => { lista.classList.remove('show'); input.setAttribute('aria-expanded', 'false'); attivo = -1; };
        const evidenzia = i => { attivo = i; lista.querySelectorAll('.dropdown-item').forEach((el, j) => el.classList.toggle('active', j === i)); };
        const scegli = i => {
            const r = risultati[i];
            if (!r) return;
            input.value = r.via;
            const cap = campo('cap', gruppo);
            if (cap && r.cap) cap.value = r.cap;
            impostaComune(gruppo, r.comune, r.provincia);
            chiudi();
        };
        const mostra = () => {
            lista.replaceChildren(...risultati.map((r, i) => {
                const li = document.createElement('li'), b = document.createElement('button');
                b.type = 'button'; b.className = 'dropdown-item'; b.setAttribute('role', 'option');
                b.innerHTML = '<i class="ri-map-pin-line" aria-hidden="true"></i> ';
                b.append(r.etichetta);
                // mousedown (non click): arriva prima del blur del campo, che chiuderebbe l'elenco.
                b.addEventListener('mousedown', e => { e.preventDefault(); scegli(i); });
                li.append(b); return li;
            }));
            const vuoto = risultati.length === 0;
            lista.classList.toggle('show', !vuoto);
            input.setAttribute('aria-expanded', String(!vuoto));
        };
        // Si cerca solo quando si smette di scrivere (350 ms) e da 4 caratteri, come richiede il server.
        input.addEventListener('input', () => {
            clearTimeout(timer);
            const q = input.value.trim();
            if (q.length < 4) { chiudi(); return; }
            timer = setTimeout(async () => {
                ultima = q;
                try {
                    const r = await fetch(urlVie + '?q=' + encodeURIComponent(q), { headers: { Accept: 'application/json' } });
                    if (!r.ok || q !== ultima) return; // nel frattempo è partita una ricerca più recente: questa risposta si ignora
                    risultati = await r.json(); attivo = -1; mostra();
                } catch { chiudi(); }
            }, 350);
        });
        input.addEventListener('keydown', e => {
            if (!lista.classList.contains('show')) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); evidenzia(Math.min(attivo + 1, risultati.length - 1)); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); evidenzia(Math.max(attivo - 1, 0)); }
            else if (e.key === 'Enter' && attivo >= 0) { e.preventDefault(); scegli(attivo); }
            else if (e.key === 'Escape') chiudi();
        });
        input.addEventListener('blur', () => setTimeout(chiudi, 150));
    }

    $(function () {
        if (!$.fn.select2) return;
        preparaSelect();
        document.querySelectorAll('input[data-indirizzo]').forEach(preparaVia);
    });
})(jQuery);
