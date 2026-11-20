// Grafici della panoramica con ApexCharts (stessa libreria e colori di ConsulensNew).
// I dati arrivano da blocchi <script type="application/json"> generati dal server.
(function () {
    if (typeof ApexCharts === 'undefined') return;

    // Colori dei grafici di ConsulensNew. Pastello: lo stacco bianco tra i segmenti, la legenda,
    // il tooltip e la tabella dei valori garantiscono la lettura anche a chi distingue male i colori.
    var COLORI = { ferie: '#6096FF', malattia: '#FF8553', rol: '#56E39F', altre: '#FFC05C', disponibili: '#BAD1FF' };
    var TESTO = '#7f6670', GRIGLIA = '#f1ebe4';
    var ore = function (v) { return (Math.round(v * 10) / 10).toLocaleString('it-IT') + ' h'; };
    var leggi = function (id) {
        var el = document.getElementById(id);
        if (!el) return null;
        try { return JSON.parse(el.textContent); } catch (e) { return null; }
    };
    var base = {
        chart: { fontFamily: 'Poppins, sans-serif', foreColor: TESTO, toolbar: { show: false }, zoom: { enabled: false },
                 animations: { enabled: true, speed: 400 } },
        dataLabels: { enabled: false },
        grid: { borderColor: GRIGLIA, strokeDashArray: 0 },
        legend: { position: 'top', horizontalAlign: 'left', fontSize: '12px', markers: { size: 6, shape: 'square' }, itemMargin: { horizontal: 10 } },
        stroke: { show: true, width: 2, colors: ['#fff'] }, // 2px di stacco bianco tra segmenti
        states: { hover: { filter: { type: 'darken', value: 0.9 } } },
    };
    var unisci = function (a, b) { return Object.assign({}, a, b, { chart: Object.assign({}, a.chart, b.chart) }); };

    // Assenze per mese: colonne impilate.
    var a = leggi('datiGraficoAssenze');
    if (a) {
        new ApexCharts(document.getElementById('graficoAssenze'), unisci(base, {
            chart: { type: 'bar', stacked: true, height: 300 },
            series: a.serie,
            colors: [COLORI.ferie, COLORI.malattia, COLORI.rol, COLORI.altre],
            plotOptions: { bar: { columnWidth: '45%', borderRadius: 4, borderRadiusApplication: 'end', borderRadiusWhenStacked: 'last' } },
            xaxis: { categories: a.mesi, axisBorder: { color: GRIGLIA }, axisTicks: { show: false } },
            yaxis: { min: 0, forceNiceScale: true, labels: { formatter: function (v) { return Math.round(v).toLocaleString('it-IT'); } },
                     title: { text: 'ore', style: { fontWeight: 400, color: TESTO } } },
            tooltip: {
                shared: true, intersect: false,
                x: { formatter: function (v, o) { return a.periodi[o.dataPointIndex]; } },
                y: { formatter: function (v) { return ore(v); } },
            },
        })).render();
    }

    // Ferie per dipendente: barre orizzontali impilate (godute + disponibili); clic = scheda del dipendente.
    var f = leggi('datiGraficoFerie');
    if (f) {
        new ApexCharts(document.getElementById('graficoFerie'), unisci(base, {
            chart: {
                type: 'bar', stacked: true, height: Math.max(160, f.nomi.length * 34 + 70),
                events: { dataPointSelection: function (e, ctx, cfg) { var u = f.link[cfg.dataPointIndex]; if (u) window.location.href = u; } },
            },
            series: f.serie,
            colors: [COLORI.ferie, COLORI.disponibili],
            plotOptions: { bar: { horizontal: true, barHeight: '55%', borderRadius: 4, borderRadiusApplication: 'end', borderRadiusWhenStacked: 'last' } },
            xaxis: { categories: f.nomi, labels: { formatter: function (v) { return Math.round(v).toLocaleString('it-IT'); } },
                     axisBorder: { color: GRIGLIA }, axisTicks: { show: false } },
            yaxis: { labels: { maxWidth: 140, style: { colors: '#512d38' } } },
            tooltip: {
                shared: true, intersect: false,
                x: { formatter: function (v, o) { var i = o.dataPointIndex; return f.nomi[i] + (f.mostraAzienda ? ' · ' + f.aziende[i] : ''); } },
                y: { formatter: function (v) { return ore(v); } },
            },
        })).render();
    }
})();
