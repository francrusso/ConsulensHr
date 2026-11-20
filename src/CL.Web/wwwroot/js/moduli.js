// Campi che compaiono solo per certe scelte: <select data-mostra="idCampo" data-valore="Valore"> o <input type="checkbox" data-mostra="idCampo">.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-mostra]').forEach(function (c) {
        const bersaglio = document.getElementById(c.dataset.mostra);
        if (!bersaglio) return;
        const aggiorna = () => { bersaglio.hidden = c.type === 'checkbox' ? !c.checked : c.value !== c.dataset.valore; };
        c.addEventListener('change', aggiorna);
        aggiorna();
    });
});
