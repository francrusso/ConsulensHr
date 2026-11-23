// Campi che compaiono solo per certe scelte: <select data-mostra="idCampo" data-valore="Valore"> o <input type="checkbox" data-mostra="idCampo">.
// Più campi sulla stessa select: data-mostra="campoA campoB" data-valore="ValoreA ValoreB" (a coppie).
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-mostra]').forEach(function (c) {
        const bersagli = c.dataset.mostra.split(' ');
        const valori = (c.dataset.valore || '').split(' ');
        bersagli.forEach(function (id, i) {
            const bersaglio = document.getElementById(id);
            if (!bersaglio) return;
            const valore = valori[i] ?? valori[0];
            const aggiorna = () => { bersaglio.hidden = c.type === 'checkbox' ? !c.checked : c.value !== valore; };
            c.addEventListener('change', aggiorna);
            aggiorna();
        });
    });
});
