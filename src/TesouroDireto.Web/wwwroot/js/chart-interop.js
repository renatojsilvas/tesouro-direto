window.chartInterop = {
    _instances: {},

    createLineChart: function (canvasId, labels, datasets) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
        }

        // Pre-process: keep only ~10 evenly spaced labels, blank the rest
        var total = labels.length;
        var step = Math.max(1, Math.floor(total / 10));
        var displayLabels = labels.map(function (label, i) {
            return (i % step === 0 || i === total - 1) ? label : '';
        });

        this._instances[canvasId] = new Chart(canvas, {
            type: 'line',
            data: {
                labels: displayLabels,
                datasets: datasets.map(function (ds) {
                    return {
                        label: ds.label,
                        data: ds.data,
                        borderColor: ds.color,
                        backgroundColor: ds.color + '20',
                        tension: 0.1,
                        pointRadius: 0,
                        borderWidth: 2,
                        fill: false
                    };
                })
            },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                aspectRatio: 3,
                interaction: {
                    intersect: false,
                    mode: 'index'
                },
                scales: {
                    x: {
                        ticks: {
                            maxRotation: 45,
                            minRotation: 0,
                            autoSkip: false
                        },
                        grid: { display: false }
                    },
                    y: {
                        beginAtZero: false,
                        ticks: {
                            callback: function (value) {
                                return 'R$ ' + value.toLocaleString('pt-BR', { minimumFractionDigits: 2 });
                            }
                        }
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            title: function (items) {
                                return labels[items[0].dataIndex];
                            },
                            label: function (context) {
                                return context.dataset.label + ': R$ ' +
                                    context.parsed.y.toLocaleString('pt-BR', { minimumFractionDigits: 2 });
                            }
                        }
                    }
                }
            }
        });
    },

    destroyChart: function (canvasId) {
        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
            delete this._instances[canvasId];
        }
    }
};
