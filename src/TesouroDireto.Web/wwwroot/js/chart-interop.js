window.chartInterop = {
    _instances: {},

    createLineChart: function (canvasId, labels, datasets) {
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
        }

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
                        backgroundColor: ds.color + '15',
                        tension: 0.3,
                        pointRadius: 0,
                        borderWidth: 2,
                        fill: true
                    };
                })
            },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                aspectRatio: 2.5,
                interaction: {
                    intersect: false,
                    mode: 'index'
                },
                scales: {
                    x: {
                        ticks: {
                            maxRotation: 45,
                            minRotation: 0,
                            autoSkip: false,
                            color: '#6a6a80',
                            font: { family: 'Inter', size: 11 }
                        },
                        grid: {
                            display: true,
                            color: 'rgba(255, 255, 255, 0.04)',
                            drawBorder: false
                        }
                    },
                    y: {
                        beginAtZero: false,
                        ticks: {
                            color: '#6a6a80',
                            font: { family: 'Inter', size: 11 },
                            callback: function (value) {
                                return 'R$ ' + value.toLocaleString('pt-BR', { minimumFractionDigits: 2 });
                            }
                        },
                        grid: {
                            color: 'rgba(255, 255, 255, 0.04)',
                            drawBorder: false
                        }
                    }
                },
                plugins: {
                    legend: {
                        labels: {
                            color: '#8888a0',
                            font: { family: 'Inter', size: 12, weight: '500' },
                            usePointStyle: true,
                            pointStyle: 'circle',
                            padding: 20
                        }
                    },
                    tooltip: {
                        backgroundColor: 'rgba(26, 26, 46, 0.95)',
                        titleColor: '#e8e8f0',
                        bodyColor: '#8888a0',
                        borderColor: 'rgba(255, 255, 255, 0.1)',
                        borderWidth: 1,
                        cornerRadius: 8,
                        padding: 12,
                        titleFont: { family: 'Inter', size: 13, weight: '600' },
                        bodyFont: { family: 'Inter', size: 12 },
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
