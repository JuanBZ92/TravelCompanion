(() => {
    const root = document.querySelector("[data-trip-planner]");
    const stateElement = document.getElementById("trip-planner-state");
    if (!root || !stateElement) {
        return;
    }

    const state = JSON.parse(stateElement.textContent);
    const payload = state.payload;
    const recommendations = state.recommendations ?? [];
    const recommendationsById = new Map(recommendations.map((item) => [item.id, item]));
    const periods = [
        { key: "morning", label: "Mañana", hint: "Desayuno / café", startsAt: "09:00" },
        { key: "midday", label: "Medio día", hint: "Almuerzo", startsAt: "12:30" },
        { key: "afternoon", label: "Tarde", hint: "Paseo / café", startsAt: "15:30" },
        { key: "night", label: "Noche", hint: "Cena / salida", startsAt: "19:30" }
    ];
    const dayList = root.querySelector("[data-day-list]");
    const daySummary = root.querySelector("[data-days-summary]");
    const dayEditor = root.querySelector("[data-day-editor]");
    const inspector = root.querySelector("[data-inspector]");
    const draftField = root.querySelector("[data-draft-json]");
    const dirtyIndicator = root.querySelector("[data-dirty-indicator]");
    const metadataPanel = root.querySelector("[data-metadata-panel]");
    let selectedDayIndex = 0;
    let activeBlockKey = null;
    let inspectorMode = null;
    let editingItem = null;
    let editingItemIndex = -1;
    let distanceFilter = "all";
    let searchFilter = "";
    let categoryFilter = "";
    let isDirty = false;

    const escapeHtml = (value) => String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");

    const normalize = (value) => String(value ?? "")
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, " ")
        .trim();

    const formatDay = (date) => new Intl.DateTimeFormat("es-ES", {
        weekday: "long",
        day: "numeric",
        month: "short",
        timeZone: "UTC"
    }).format(new Date(`${date}T00:00:00Z`));

    const shortDay = (date) => new Intl.DateTimeFormat("es-ES", {
        weekday: "short",
        day: "2-digit",
        month: "2-digit",
        timeZone: "UTC"
    }).format(new Date(`${date}T00:00:00Z`));

    const newId = () => crypto.randomUUID();

    function setDirty() {
        isDirty = true;
        dirtyIndicator.hidden = false;
        const status = root.querySelector("[data-draft-status]");
        if (status) {
            status.textContent = "Editando";
            status.className = "planner-status is-draft";
        }
    }

    function createBlock(period) {
        return {
            id: newId(),
            periodKey: period.key,
            curatedDescription: "",
            autofillEnabled: true,
            recommendations: [],
            items: []
        };
    }

    function createDay(date, dayNumber) {
        return {
            id: newId(),
            date,
            dayNumber,
            city: "",
            hotelBase: "",
            baseLatitude: null,
            baseLongitude: null,
            introduction: "",
            blocks: periods.map(createBlock)
        };
    }

    function reconcileDays() {
        const start = new Date(`${payload.startsOn}T00:00:00Z`);
        const end = new Date(`${payload.endsOn}T00:00:00Z`);
        if (Number.isNaN(start.valueOf()) || Number.isNaN(end.valueOf()) || end < start) {
            return;
        }

        const existing = new Map(payload.days.map((day) => [day.date, day]));
        const days = [];
        for (let cursor = new Date(start); cursor <= end; cursor.setUTCDate(cursor.getUTCDate() + 1)) {
            const date = cursor.toISOString().slice(0, 10);
            const day = existing.get(date) ?? createDay(date, days.length + 1);
            const previousDay = days.at(-1);
            if (!existing.has(date) && previousDay) {
                day.city = previousDay.city;
                day.hotelBase = previousDay.hotelBase;
                day.baseLatitude = previousDay.baseLatitude;
                day.baseLongitude = previousDay.baseLongitude;
            }
            day.dayNumber = days.length + 1;
            day.blocks = periods.map((period) => day.blocks.find((block) => block.periodKey === period.key) ?? createBlock(period));
            days.push(day);
        }
        payload.days = days;
        selectedDayIndex = Math.min(selectedDayIndex, Math.max(0, days.length - 1));
    }

    function selectedDay() {
        return payload.days[selectedDayIndex];
    }

    function selectedBlock() {
        return selectedDay()?.blocks.find((block) => block.periodKey === activeBlockKey) ?? null;
    }

    function toNumber(value) {
        if (value === "" || value === null || value === undefined) {
            return null;
        }
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function distanceKm(first, second) {
        if (!first || !second || first.latitude == null || first.longitude == null || second.latitude == null || second.longitude == null) {
            return null;
        }
        const radians = (degrees) => degrees * Math.PI / 180;
        const deltaLatitude = radians(second.latitude - first.latitude);
        const deltaLongitude = radians(second.longitude - first.longitude);
        const latitude1 = radians(first.latitude);
        const latitude2 = radians(second.latitude);
        const a = Math.sin(deltaLatitude / 2) ** 2
            + Math.cos(latitude1) * Math.cos(latitude2) * Math.sin(deltaLongitude / 2) ** 2;
        return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    function dayAnchors(day) {
        const anchors = [];
        if (day.baseLatitude != null && day.baseLongitude != null) {
            anchors.push({ latitude: Number(day.baseLatitude), longitude: Number(day.baseLongitude) });
        }
        for (const block of day.blocks) {
            for (const assignment of block.recommendations) {
                const recommendation = recommendationsById.get(assignment.recommendationId);
                if (recommendation) {
                    anchors.push({ latitude: Number(recommendation.latitude), longitude: Number(recommendation.longitude) });
                }
            }
            for (const item of block.items) {
                if (item.latitude != null && item.longitude != null) {
                    anchors.push({ latitude: Number(item.latitude), longitude: Number(item.longitude) });
                }
            }
        }
        return anchors;
    }

    function dayMetrics(day) {
        const anchors = dayAnchors(day);
        let totalDistance = 0;
        let longJumps = 0;
        for (let index = 1; index < anchors.length; index++) {
            const distance = distanceKm(anchors[index - 1], anchors[index]);
            if (distance != null) {
                totalDistance += distance;
                if (distance > 5) {
                    longJumps++;
                }
            }
        }
        const planned = day.blocks.reduce((total, block) => total + block.recommendations.length + block.items.length, 0);
        const completedBlocks = day.blocks.filter((block) => block.recommendations.length > 0 || block.items.length > 0 || block.curatedDescription.trim()).length;
        return { anchors: anchors.length, totalDistance, longJumps, planned, completedBlocks };
    }

    function currentAnchor(day, block) {
        const periodIndex = periods.findIndex((period) => period.key === block.periodKey);
        for (let index = periodIndex; index >= 0; index--) {
            const candidateBlock = day.blocks.find((item) => item.periodKey === periods[index].key);
            const itemAnchor = [...candidateBlock.items].reverse().find((item) => item.latitude != null && item.longitude != null);
            if (itemAnchor) {
                return { latitude: Number(itemAnchor.latitude), longitude: Number(itemAnchor.longitude), label: itemAnchor.title };
            }
            const assignment = [...candidateBlock.recommendations].reverse()[0];
            const recommendation = assignment ? recommendationsById.get(assignment.recommendationId) : null;
            if (recommendation) {
                return { latitude: Number(recommendation.latitude), longitude: Number(recommendation.longitude), label: recommendation.title };
            }
        }
        if (day.baseLatitude != null && day.baseLongitude != null) {
            return { latitude: Number(day.baseLatitude), longitude: Number(day.baseLongitude), label: day.hotelBase || "base del día" };
        }
        return null;
    }

    function usedRecommendation(recommendationId) {
        for (const day of payload.days) {
            for (const block of day.blocks) {
                if (block.recommendations.some((item) => item.recommendationId === recommendationId)) {
                    return { day, block };
                }
            }
        }
        return null;
    }

    function isPeriodMatch(recommendation, periodKey) {
        const values = new Set((recommendation.tags ?? []).map(normalize));
        values.add(normalize(recommendation.category));
        if (periodKey === "morning") {
            return ["breakfast", "cafe", "coffee", "bakery", "food"].some((tag) => values.has(tag));
        }
        if (periodKey === "midday") {
            return ["lunch", "food", "restaurant", "market", "ramen", "sushi"].some((tag) => values.has(tag));
        }
        if (periodKey === "night") {
            return ["nightlife", "bar", "dinner", "food", "izakaya"].some((tag) => values.has(tag));
        }
        return !values.has("breakfast") || values.size > 1;
    }

    function renderDayList() {
        const cityCount = new Set(payload.days.map((day) => normalize(day.city)).filter(Boolean)).size;
        daySummary.textContent = `${payload.days.length} días · ${cityCount} ciudades`;
        dayList.innerHTML = payload.days.map((day, index) => {
            const metrics = dayMetrics(day);
            const blockDots = day.blocks.map((block) => {
                const hasContent = block.recommendations.length > 0 || block.items.length > 0 || block.curatedDescription.trim();
                return `<span class="${hasContent ? "is-filled" : ""}" title="${hasContent ? "Con contenido" : "Vacío"}"></span>`;
            }).join("");
            return `
                <button type="button" class="planner-day-link ${index === selectedDayIndex ? "is-active" : ""}" data-day-index="${index}">
                    <span class="planner-day-line"><strong>D${day.dayNumber}</strong><span>${escapeHtml(shortDay(day.date))}</span></span>
                    <span class="planner-day-city">${escapeHtml(day.city || "Ciudad pendiente")}</span>
                    <span class="planner-day-stats">
                        <span class="planner-day-dots">${blockDots}</span>
                        <span>${metrics.planned} paradas</span>
                        <span>${metrics.totalDistance > 0 ? `${metrics.totalDistance.toFixed(1)} km` : ""}</span>
                        ${metrics.longJumps > 0 ? `<span class="has-warning">${metrics.longJumps} salto${metrics.longJumps > 1 ? "s" : ""}</span>` : ""}
                    </span>
                </button>`;
        }).join("");
        dayList.querySelectorAll("[data-day-index]").forEach((button) => {
            button.addEventListener("click", () => {
                selectedDayIndex = Number(button.dataset.dayIndex);
                activeBlockKey = null;
                inspectorMode = null;
                render();
            });
        });
    }

    function renderDayEditor() {
        const day = selectedDay();
        if (!day) {
            dayEditor.innerHTML = "<p>No hay días en este viaje.</p>";
            return;
        }
        const metrics = dayMetrics(day);
        dayEditor.innerHTML = `
            <section class="planner-day-heading">
                <span class="eyebrow">Día ${day.dayNumber}</span>
                <h1>${escapeHtml(formatDay(day.date))} · ${escapeHtml(day.city || "Sin ciudad")}</h1>
                <div class="planner-day-fields">
                    <div><label>Ciudad</label><input data-day-field="city" value="${escapeHtml(day.city)}" placeholder="Tokyo" /></div>
                    <div><label>Hotel / base</label><input data-day-field="hotelBase" value="${escapeHtml(day.hotelBase)}" placeholder="Se hereda del día anterior" title="Se aplicará a los días siguientes hasta encontrar otro hotel" /></div>
                    <div><label>Latitud</label><input data-day-field="baseLatitude" type="number" step="0.000001" value="${day.baseLatitude ?? ""}" /></div>
                    <div><label>Longitud</label><input data-day-field="baseLongitude" type="number" step="0.000001" value="${day.baseLongitude ?? ""}" /></div>
                </div>
                <div class="planner-day-intro"><label>Introducción del día</label><textarea data-day-field="introduction" placeholder="Llegada, ritmo recomendado y contexto general...">${escapeHtml(day.introduction)}</textarea></div>
                <div class="planner-metrics">
                    <span class="planner-metric"><strong>${metrics.anchors}</strong><span>anclas</span></span>
                    <span class="planner-metric"><strong>${metrics.planned}</strong><span>paradas</span></span>
                    <span class="planner-metric"><strong>${metrics.totalDistance.toFixed(1)}</strong><span>km aprox.</span></span>
                    <span class="planner-metric"><strong>${metrics.longJumps}</strong><span>saltos largos</span></span>
                </div>
            </section>
            <div class="planner-blocks">
                ${day.blocks.map((block) => renderBlock(day, block)).join("")}
            </div>`;

        dayEditor.querySelectorAll("[data-day-field]").forEach((input) => {
            input.addEventListener("input", () => {
                const field = input.dataset.dayField;
                const previousValue = day[field];
                day[field] = field === "baseLatitude" || field === "baseLongitude" ? toNumber(input.value) : input.value;
                if (field === "hotelBase") {
                    for (let index = selectedDayIndex + 1; index < payload.days.length; index++) {
                        const nextDay = payload.days[index];
                        if (nextDay.hotelBase && nextDay.hotelBase !== previousValue) {
                            break;
                        }
                        nextDay.hotelBase = input.value;
                    }
                }
                setDirty();
            });
            input.addEventListener("change", () => renderDayList());
        });

        dayEditor.querySelectorAll("[data-block-description]").forEach((textarea) => {
            textarea.addEventListener("input", () => {
                const block = day.blocks.find((item) => item.periodKey === textarea.dataset.blockDescription);
                block.curatedDescription = textarea.value;
                setDirty();
            });
        });
        dayEditor.querySelectorAll("[data-autofill]").forEach((checkbox) => {
            checkbox.addEventListener("change", () => {
                const block = day.blocks.find((item) => item.periodKey === checkbox.dataset.autofill);
                block.autofillEnabled = checkbox.checked;
                setDirty();
            });
        });
        dayEditor.querySelectorAll("[data-open-recommendations]").forEach((button) => {
            button.addEventListener("click", () => {
                activeBlockKey = button.dataset.openRecommendations;
                inspectorMode = "recommendations";
                distanceFilter = "all";
                searchFilter = "";
                categoryFilter = "";
                renderInspector();
            });
        });
        dayEditor.querySelectorAll("[data-add-item]").forEach((button) => {
            button.addEventListener("click", () => openItemEditor(button.dataset.addItem));
        });
        dayEditor.querySelectorAll("[data-remove-recommendation]").forEach((button) => {
            button.addEventListener("click", () => {
                const block = day.blocks.find((item) => item.periodKey === button.dataset.block);
                block.recommendations = block.recommendations.filter((item) => item.recommendationId !== button.dataset.removeRecommendation);
                setDirty();
                render();
            });
        });
        dayEditor.querySelectorAll("[data-edit-item]").forEach((button) => {
            button.addEventListener("click", () => {
                const block = day.blocks.find((item) => item.periodKey === button.dataset.block);
                const index = block.items.findIndex((item) => item.id === button.dataset.editItem);
                openItemEditor(block.periodKey, index);
            });
        });
        dayEditor.querySelectorAll("[data-delete-item]").forEach((button) => {
            button.addEventListener("click", () => {
                if (!confirm("¿Quitar este elemento del borrador?")) {
                    return;
                }
                const block = day.blocks.find((item) => item.periodKey === button.dataset.block);
                block.items = block.items.filter((item) => item.id !== button.dataset.deleteItem);
                setDirty();
                render();
            });
        });
    }

    function renderBlock(day, block) {
        const period = periods.find((item) => item.key === block.periodKey);
        const hasContent = block.recommendations.length > 0 || block.items.length > 0 || block.curatedDescription.trim();
        const picks = block.recommendations.map((assignment) => {
            const recommendation = recommendationsById.get(assignment.recommendationId);
            return recommendation ? `
                <span class="planner-pick">
                    ${escapeHtml(recommendation.title)}
                    <button type="button" title="Quitar" data-block="${block.periodKey}" data-remove-recommendation="${recommendation.id}">×</button>
                </span>` : "";
        }).join("");
        const items = block.items.map((item) => `
            <div class="planner-item">
                <div class="planner-item-title">
                    <span class="planner-item-time">${escapeHtml(String(item.startsAt).slice(0, 5))}</span>
                    <span><strong>${escapeHtml(item.title || "Sin título")}</strong><small>${escapeHtml(item.type)} · ${escapeHtml(item.locationName || item.originName || "Sin lugar")}</small></span>
                </div>
                <span>
                    <button type="button" title="Editar" data-block="${block.periodKey}" data-edit-item="${item.id}">Editar</button>
                    <button type="button" title="Quitar" data-block="${block.periodKey}" data-delete-item="${item.id}">×</button>
                </span>
            </div>`).join("");
        return `
            <section class="planner-block ${hasContent ? "has-content" : ""} ${activeBlockKey === block.periodKey ? "is-active" : ""}">
                <div class="planner-block-header">
                    <div class="planner-block-title"><strong>${period.label}</strong><span>${period.hint}</span></div>
                    <label class="planner-autofill"><input type="checkbox" data-autofill="${block.periodKey}" ${block.autofillEnabled ? "checked" : ""} /> Autofill</label>
                </div>
                <div class="planner-picks">${picks}</div>
                <textarea data-block-description="${block.periodKey}" placeholder="Descripción curada para este momento del día...">${escapeHtml(block.curatedDescription)}</textarea>
                <div class="planner-items">${items}</div>
                <div class="planner-block-actions">
                    <button type="button" data-open-recommendations="${block.periodKey}">Agregar recomendación (${block.recommendations.length}/3)</button>
                    <button type="button" data-add-item="${block.periodKey}">Agregar reserva o evento</button>
                </div>
            </section>`;
    }

    function renderInspector() {
        if (inspectorMode === "recommendations") {
            renderRecommendationInspector();
        } else if (inspectorMode === "item") {
            renderItemInspector();
        } else {
            inspector.innerHTML = `<div class="planner-inspector-empty"><strong>Elegí una acción</strong><p>Agregá recomendaciones o elementos de agenda desde cualquier bloque.</p></div>`;
        }
    }

    function renderRecommendationInspector() {
        const day = selectedDay();
        const block = selectedBlock();
        const period = periods.find((item) => item.key === block.periodKey);
        const anchor = currentAnchor(day, block);
        const categories = [...new Set(recommendations.map((item) => item.category).filter(Boolean))].sort();
        const city = normalize(day.city);
        let candidates = recommendations.map((recommendation) => {
            const distance = anchor ? distanceKm(anchor, recommendation) : null;
            const used = usedRecommendation(recommendation.id);
            const selected = block.recommendations.some((item) => item.recommendationId === recommendation.id);
            let score = 0;
            if (city && (normalize(recommendation.citySlug) === city || normalize(recommendation.neighborhood).includes(city))) score += 100;
            if (isPeriodMatch(recommendation, block.periodKey)) score += 35;
            if (distance != null) score += Math.max(0, 30 - distance * 5);
            score += Number(recommendation.rating ?? 0) * 2;
            if (used && !selected) score -= 200;
            return { recommendation, distance, used, selected, score };
        });
        if (searchFilter) {
            const term = normalize(searchFilter);
            candidates = candidates.filter(({ recommendation }) => normalize(`${recommendation.title} ${recommendation.description} ${recommendation.tags.join(" ")}`).includes(term));
        }
        if (categoryFilter) {
            candidates = candidates.filter(({ recommendation }) => recommendation.category === categoryFilter);
        }
        if (distanceFilter !== "all") {
            const limit = Number(distanceFilter);
            candidates = candidates.filter((item) => item.distance != null && item.distance <= limit);
        }
        candidates.sort((left, right) => right.score - left.score || left.recommendation.title.localeCompare(right.recommendation.title));

        inspector.innerHTML = `
            <div class="planner-inspector-header">
                <strong>Recomendaciones · ${period.label} · D${day.dayNumber}</strong>
                <small>Ancla: ${escapeHtml(anchor?.label ?? day.city ?? "sin coordenadas")}</small>
            </div>
            <div class="planner-inspector-filters">
                <div class="planner-distance-filter">
                    <button type="button" data-distance="1" class="${distanceFilter === "1" ? "is-active" : ""}">≤ 1 km</button>
                    <button type="button" data-distance="2" class="${distanceFilter === "2" ? "is-active" : ""}">≤ 2 km</button>
                    <button type="button" data-distance="all" class="${distanceFilter === "all" ? "is-active" : ""}">Todas</button>
                </div>
                <input type="search" data-candidate-search value="${escapeHtml(searchFilter)}" placeholder="Buscar en ${recommendations.length} recomendaciones" />
                <select data-category-filter><option value="">Todas las categorías</option>${categories.map((category) => `<option value="${escapeHtml(category)}" ${categoryFilter === category ? "selected" : ""}>${escapeHtml(category)}</option>`).join("")}</select>
            </div>
            <div class="planner-candidates">
                ${candidates.slice(0, 100).map(({ recommendation, distance, used, selected }) => `
                    <label class="planner-candidate ${selected ? "is-selected" : ""} ${used && !selected ? "is-used" : ""}">
                        <input type="checkbox" data-candidate-id="${recommendation.id}" ${selected ? "checked" : ""} ${used && !selected ? "disabled" : ""} />
                        <span>
                            <span class="planner-candidate-title"><strong>${escapeHtml(recommendation.title)}</strong>${used && !selected ? `<small>D${used.day.dayNumber}</small>` : ""}</span>
                            <small>${distance == null ? "Sin distancia" : `${distance.toFixed(1)} km`} · ${escapeHtml(recommendation.neighborhood)} · ${escapeHtml(recommendation.priceLevel || "medium")}</small>
                            <p>${escapeHtml(recommendation.description)}</p>
                            <span>${recommendation.tags.slice(0, 6).map((tag) => `<span class="planner-tag">${escapeHtml(tag)}</span>`).join("")}</span>
                        </span>
                    </label>`).join("") || `<div class="planner-inspector-empty"><p>No hay candidatos para estos filtros.</p></div>`}
            </div>`;

        inspector.querySelectorAll("[data-distance]").forEach((button) => button.addEventListener("click", () => {
            distanceFilter = button.dataset.distance;
            renderRecommendationInspector();
        }));
        inspector.querySelector("[data-candidate-search]")?.addEventListener("input", (event) => {
            searchFilter = event.target.value;
            renderRecommendationInspector();
            const input = inspector.querySelector("[data-candidate-search]");
            input.focus();
            input.setSelectionRange(input.value.length, input.value.length);
        });
        inspector.querySelector("[data-category-filter]")?.addEventListener("change", (event) => {
            categoryFilter = event.target.value;
            renderRecommendationInspector();
        });
        inspector.querySelectorAll("[data-candidate-id]").forEach((checkbox) => checkbox.addEventListener("change", () => {
            const id = checkbox.dataset.candidateId;
            if (checkbox.checked) {
                if (block.recommendations.length >= 3) {
                    checkbox.checked = false;
                    alert("Cada bloque admite hasta 3 recomendaciones.");
                    return;
                }
                block.recommendations.push({ id: newId(), recommendationId: id });
            } else {
                block.recommendations = block.recommendations.filter((item) => item.recommendationId !== id);
            }
            setDirty();
            renderDayList();
            renderDayEditor();
            renderRecommendationInspector();
        }));
    }

    function openItemEditor(periodKey, itemIndex = -1) {
        activeBlockKey = periodKey;
        inspectorMode = "item";
        editingItemIndex = itemIndex;
        const block = selectedBlock();
        editingItem = itemIndex >= 0 ? structuredClone(block.items[itemIndex]) : {
            id: newId(),
            type: "Event",
            planningKind: "ConfirmedReservation",
            startsAt: periods.find((period) => period.key === periodKey).startsAt,
            endsOn: null,
            endsAt: null,
            title: "",
            city: selectedDay().city,
            locationName: "",
            address: "",
            confirmationCode: "",
            notes: "",
            latitude: null,
            longitude: null,
            airline: "",
            flightNumber: "",
            originName: "",
            destinationName: "",
            originAirport: "",
            destinationAirport: ""
        };
        renderItemInspector();
    }

    function renderItemInspector() {
        const item = editingItem;
        const isFlight = item.type === "Flight";
        const isLodging = item.type === "Lodging";
        inspector.innerHTML = `
            <div class="planner-inspector-header">
                <strong>${editingItemIndex >= 0 ? "Editar" : "Nuevo"} elemento · D${selectedDay().dayNumber}</strong>
                <small>Reserva, vuelo, hospedaje o evento manual</small>
            </div>
            <div class="planner-item-form">
                <div><label>Tipo</label><select data-item-field="type"><option value="Event" ${item.type === "Event" ? "selected" : ""}>Evento</option><option value="Flight" ${isFlight ? "selected" : ""}>Vuelo</option><option value="Lodging" ${isLodging ? "selected" : ""}>Hospedaje</option></select></div>
                <div><label>Naturaleza</label><select data-item-field="planningKind" ${isFlight || isLodging ? "disabled" : ""}><option value="ConfirmedReservation" ${item.planningKind === "ConfirmedReservation" ? "selected" : ""}>Reserva confirmada</option><option value="ManualEvent" ${item.planningKind === "ManualEvent" ? "selected" : ""}>Evento manual</option></select></div>
                <div class="is-wide"><label>Título</label><input data-item-field="title" value="${escapeHtml(item.title)}" /></div>
                <div><label>Hora</label><input data-item-field="startsAt" type="time" value="${escapeHtml(String(item.startsAt).slice(0, 5))}" /></div>
                <div><label>Hora fin</label><input data-item-field="endsAt" type="time" value="${escapeHtml(item.endsAt ? String(item.endsAt).slice(0, 5) : "")}" /></div>
                ${isLodging ? `<div><label>Checkout</label><input data-item-field="endsOn" type="date" value="${escapeHtml(item.endsOn ?? "")}" /></div>` : ""}
                ${isFlight ? `
                    <div><label>Aerolínea</label><input data-item-field="airline" value="${escapeHtml(item.airline)}" /></div>
                    <div><label>Vuelo</label><input data-item-field="flightNumber" value="${escapeHtml(item.flightNumber)}" /></div>
                    <div><label>Origen</label><input data-item-field="originName" value="${escapeHtml(item.originName)}" /></div>
                    <div><label>Destino</label><input data-item-field="destinationName" value="${escapeHtml(item.destinationName)}" /></div>
                    <div><label>Aeropuerto origen</label><input data-item-field="originAirport" value="${escapeHtml(item.originAirport)}" /></div>
                    <div><label>Aeropuerto destino</label><input data-item-field="destinationAirport" value="${escapeHtml(item.destinationAirport)}" /></div>` : `
                    <div class="is-wide"><label>Lugar</label><input data-item-field="locationName" value="${escapeHtml(item.locationName)}" /></div>`}
                <div class="is-wide"><label>Dirección</label><input data-item-field="address" value="${escapeHtml(item.address)}" /></div>
                <div><label>Ciudad</label><input data-item-field="city" value="${escapeHtml(item.city)}" /></div>
                <div><label>Código</label><input data-item-field="confirmationCode" value="${escapeHtml(item.confirmationCode)}" /></div>
                <div><label>Latitud</label><input data-item-field="latitude" type="number" step="0.000001" value="${item.latitude ?? ""}" /></div>
                <div><label>Longitud</label><input data-item-field="longitude" type="number" step="0.000001" value="${item.longitude ?? ""}" /></div>
                <div class="is-wide"><label>Notas</label><textarea data-item-field="notes">${escapeHtml(item.notes)}</textarea></div>
                <div class="planner-item-form-actions"><button type="button" data-save-item>Guardar elemento</button><button type="button" class="secondary" data-cancel-item>Cancelar</button></div>
            </div>`;

        inspector.querySelectorAll("[data-item-field]").forEach((input) => {
            input.addEventListener("input", () => updateEditingItem(input));
            input.addEventListener("change", () => {
                updateEditingItem(input);
                if (input.dataset.itemField === "type") {
                    if (editingItem.type === "Flight" || editingItem.type === "Lodging") {
                        editingItem.planningKind = "ConfirmedReservation";
                    }
                    renderItemInspector();
                }
            });
        });
        inspector.querySelector("[data-save-item]").addEventListener("click", saveEditingItem);
        inspector.querySelector("[data-cancel-item]").addEventListener("click", () => {
            editingItem = null;
            editingItemIndex = -1;
            inspectorMode = null;
            renderInspector();
        });
    }

    function updateEditingItem(input) {
        const field = input.dataset.itemField;
        if (field === "latitude" || field === "longitude") {
            editingItem[field] = toNumber(input.value);
        } else {
            editingItem[field] = input.value || null;
        }
    }

    function saveEditingItem() {
        if (!String(editingItem.title ?? "").trim()) {
            alert("Completá el título.");
            return;
        }
        if (editingItem.type !== "Flight" && !String(editingItem.locationName ?? "").trim()) {
            alert("Completá el lugar.");
            return;
        }
        const block = selectedBlock();
        if (editingItemIndex >= 0) {
            block.items[editingItemIndex] = editingItem;
        } else {
            block.items.push(editingItem);
        }
        setDirty();
        editingItem = null;
        editingItemIndex = -1;
        inspectorMode = null;
        render();
    }

    function bindMetadata() {
        metadataPanel.querySelectorAll("[data-meta]").forEach((input) => {
            const field = input.dataset.meta;
            input.value = payload[field] ?? "";
            input.addEventListener("input", () => {
                payload[field] = input.value;
                setDirty();
                if (field === "travelerName") {
                    root.querySelector("[data-trip-name]").textContent = `· ${input.value}`;
                }
            });
            input.addEventListener("change", () => {
                if (field === "startsOn" || field === "endsOn") {
                    reconcileDays();
                    render();
                }
            });
        });
        root.querySelector("[data-toggle-metadata]").addEventListener("click", () => {
            metadataPanel.hidden = !metadataPanel.hidden;
        });
        root.querySelector("[name='NewAccessPin']")?.addEventListener("input", setDirty);
    }

    function render() {
        renderDayList();
        renderDayEditor();
        renderInspector();
    }

    root.querySelector("[data-planner-form]").addEventListener("submit", (event) => {
        draftField.value = JSON.stringify(payload);
        if (event.submitter?.matches("[data-publish]") && !confirm("¿Aplicar este borrador a la app del viajero?")) {
            event.preventDefault();
            return;
        }
        isDirty = false;
    });

    root.querySelector("[data-discard-form]")?.addEventListener("submit", (event) => {
        if (!confirm("¿Descartar el borrador? Esta acción no se puede deshacer.")) {
            event.preventDefault();
        }
    });

    window.addEventListener("beforeunload", (event) => {
        if (!isDirty) {
            return;
        }
        event.preventDefault();
        event.returnValue = "";
    });

    reconcileDays();
    bindMetadata();
    render();
})();
