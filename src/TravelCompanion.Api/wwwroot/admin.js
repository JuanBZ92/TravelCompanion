const reservationTypeSelect = document.querySelector("[data-reservation-type]");

function updateReservationSections() {
    if (!reservationTypeSelect) {
        return;
    }

    const selectedType = reservationTypeSelect.value;
    document.querySelectorAll("[data-reservation-section]").forEach((section) => {
        const visibleTypes = section.dataset.reservationSection
            .split(",")
            .map((value) => value.trim());

        section.hidden = !visibleTypes.includes(selectedType);
    });
}

if (reservationTypeSelect) {
    reservationTypeSelect.addEventListener("change", updateReservationSections);
    updateReservationSections();
}

const recommendationAccessSelect = document.querySelector("[data-recommendation-access]");
const recommendationPackageField = document.querySelector("[data-recommendation-package-field]");

function updateRecommendationPackageField() {
    if (!recommendationAccessSelect || !recommendationPackageField) {
        return;
    }

    const isPackageAccess = recommendationAccessSelect.value === "Paid";
    recommendationPackageField.hidden = !isPackageAccess;
}

if (recommendationAccessSelect) {
    recommendationAccessSelect.addEventListener("change", updateRecommendationPackageField);
    updateRecommendationPackageField();
}
