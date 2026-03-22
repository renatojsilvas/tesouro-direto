window.chartInterop = {
    _instances: {},

    createLineChart: function (canvasId, labels, datasets) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
        }

        this._instances[canvasId] = new Chart(canvas, {
            type: 'line',
            data: {
                labels: labels,
                datasets: datasets.map(function (ds) {
                    return {
                        label: ds.label,
                        data: ds.data,
                        borderColor: ds.color,
                        backgroundColor: ds.color + '20',
                        tension: 0.1,
                        pointRadius: 0,
                        borderWidth: 2
                    };
                })
            },
            options: {
                responsive: true,
                interaction: {
                    intersect: false,
                    mode: 'index'
                },
                scales: {
                    x: {
                        ticks: {
                            maxTicksLimit: 12
                        }
                    },
                    y: {
                        beginAtZero: false
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
