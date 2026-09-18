// Global variables
let map;
let connection;
const markers = {
    incidents: {},
    crews: {}
};
const routeLines = {}; // Store polylines for active assignments
let chatAttachedFile = null;

// Media Upload Helper
async function uploadFile(file) {
    const formData = new FormData();
    formData.append("file", file);
    
    const res = await fetch("/api/media/upload", {
        method: "POST",
        body: formData
    });
    
    if (!res.ok) {
        throw new Error("Server error occurred while uploading file.");
    }
    
    const data = await res.json();
    return data.imageUrl;
}

function getPopupMediaHtml(imageUrl) {
    if (!imageUrl) return "";
    const isVideo = imageUrl.endsWith(".mp4") || imageUrl.endsWith(".webm");
    if (isVideo) {
        return `
            <div class="media-thumbnail-container" onclick="openLightbox('${imageUrl}')">
                <video src="${imageUrl}" muted playsinline></video>
                <div class="media-thumbnail-overlay">
                    <i class="fa-solid fa-play"></i>
                </div>
            </div>
        `;
    } else {
        return `
            <div class="media-thumbnail-container" onclick="openLightbox('${imageUrl}')">
                <img src="${imageUrl}">
                <div class="media-thumbnail-overlay">
                    <i class="fa-solid fa-expand"></i>
                </div>
            </div>
        `;
    }
}

window.openLightbox = function(url) {
    const lightbox = document.getElementById("media-lightbox");
    const content = lightbox.querySelector(".lightbox-content");
    if (!lightbox || !content) return;
    
    const isVideo = url.endsWith(".mp4") || url.endsWith(".webm");
    if (isVideo) {
        content.innerHTML = `<video src="${url}" controls autoplay style="max-width:100%; max-height:80vh;"></video>`;
    } else {
        content.innerHTML = `<img src="${url}" style="max-width:100%; max-height:80vh;">`;
    }
    lightbox.classList.remove("hidden");
};

window.closeLightbox = function() {
    const lightbox = document.getElementById("media-lightbox");
    if (lightbox) {
        lightbox.classList.add("hidden");
        const content = lightbox.querySelector(".lightbox-content");
        if (content) content.innerHTML = "";
    }
};

window.clearChatAttachment = function() {
    chatAttachedFile = null;
    const fileInput = document.getElementById("chat-file-input");
    if (fileInput) fileInput.value = "";
    const previewContainer = document.getElementById("chat-attachment-preview");
    if (previewContainer) {
        previewContainer.classList.add("hidden");
        previewContainer.innerHTML = "";
    }
};

// Custom marker icons using FontAwesome + CSS animations
function createCrewIcon(type, status) {
    let iconClass = "fa-truck-pickup";
    const typeStr = (type || "").toString().toLowerCase();
    if (typeStr.includes("zoning") || typeStr.includes("imar")) iconClass = "fa-compass-drafting";
    if (typeStr.includes("publicworks") || typeStr.includes("fen")) iconClass = "fa-road-barrier";
    if (typeStr.includes("parks") || typeStr.includes("park")) iconClass = "fa-tree";
    if (typeStr.includes("sanitation") || typeStr.includes("temizlik")) iconClass = "fa-trash-can";

    const statusClass = (status || "").toLowerCase();
    
    return L.divIcon({
        html: `
            <div class="map-marker crew ${statusClass}">
                <div class="marker-circle">
                    <i class="fa-solid ${iconClass}"></i>
                </div>
                <div class="pulse-ring"></div>
            </div>
        `,
        className: 'custom-marker-wrapper',
        iconSize: [36, 36],
        iconAnchor: [18, 18],
        popupAnchor: [0, -18]
    });
}

function createIncidentIcon(status) {
    const statusClass = (status || "").toLowerCase();
    return L.divIcon({
        html: `
            <div class="map-marker incident ${statusClass}">
                <div class="marker-circle">
                    <i class="fa-solid fa-triangle-exclamation"></i>
                </div>
                <div class="pulse-ring"></div>
            </div>
        `,
        className: 'custom-marker-wrapper',
        iconSize: [36, 36],
        iconAnchor: [18, 18],
        popupAnchor: [0, -18]
    });
}

// Initialise Application
document.addEventListener("DOMContentLoaded", () => {
    setupAuth();
    
    // Check if user session already exists
    const user = checkSession();
    if (user) {
        loadDashboard(user);
    }
});

// Check local storage session
function checkSession() {
    const userStr = localStorage.getItem("citypulse_user");
    if (userStr) {
        try {
            return JSON.parse(userStr);
        } catch (e) {
            localStorage.removeItem("citypulse_user");
        }
    }
    return null;
}

// Setup login/register screen handlers
function setupAuth() {
    const loginForm = document.getElementById("login-form");
    const registerForm = document.getElementById("register-form");
    const switchToRegister = document.getElementById("switch-to-register");
    const switchToLogin = document.getElementById("switch-to-login");
    const authOverlay = document.getElementById("auth-overlay");
    const appMain = document.getElementById("app-main");
    const btnLogout = document.getElementById("btn-logout");

    switchToRegister.addEventListener("click", (e) => {
        e.preventDefault();
        loginForm.classList.add("hidden");
        registerForm.classList.remove("hidden");
    });

    switchToLogin.addEventListener("click", (e) => {
        e.preventDefault();
        registerForm.classList.add("hidden");
        loginForm.classList.remove("hidden");
    });

    loginForm.addEventListener("submit", async (e) => {
        e.preventDefault();
        const username = document.getElementById("login-username").value.trim();
        const password = document.getElementById("login-password").value.trim();
        const errorDiv = document.getElementById("login-error");
        errorDiv.innerText = "";

        try {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ username, password })
            });
            const data = await res.json();
            if (res.ok && data.success) {
                localStorage.setItem("citypulse_user", JSON.stringify(data));
                loadDashboard(data);
            } else {
                errorDiv.innerText = data.error || "Login failed.";
            }
        } catch (err) {
            errorDiv.innerText = "Could not connect to API services. Ensure server is running.";
        }
    });

    registerForm.addEventListener("submit", async (e) => {
        e.preventDefault();
        const username = document.getElementById("register-username").value.trim();
        const password = document.getElementById("register-password").value.trim();
        const role = parseInt(document.getElementById("register-role").value);
        const errorDiv = document.getElementById("register-error");
        errorDiv.innerText = "";

        try {
            const res = await fetch("/api/auth/register", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ username, password, role })
            });
            const data = await res.json();
            if (res.ok && data.success) {
                // Switch to login form
                registerForm.classList.add("hidden");
                loginForm.classList.remove("hidden");
                document.getElementById("login-username").value = username;
                document.getElementById("login-password").value = password;
                document.getElementById("login-error").innerText = "Registration successful! You can now sign in.";
                document.getElementById("login-error").style.color = "#10b981";
            } else {
                errorDiv.innerText = data.error || "Registration failed.";
            }
        } catch (err) {
            errorDiv.innerText = "Could not connect to API services.";
        }
    });

    btnLogout.addEventListener("click", () => {
        localStorage.removeItem("citypulse_user");
        
        // Reset forms
        document.getElementById("login-username").value = "";
        document.getElementById("login-password").value = "";
        document.getElementById("login-error").innerText = "";
        
        // Clear active dispatches tracking widget
        const trackingPanel = document.getElementById("dispatch-tracking-panel");
        const trackingContainer = document.getElementById("dispatch-tracking-container");
        if (trackingPanel) trackingPanel.style.display = "none";
        if (trackingContainer) trackingContainer.innerHTML = "";

        // Show login screen
        authOverlay.classList.remove("hidden");
        appMain.classList.add("hidden");
        
        // Disconnect SignalR hub
        if (connection) {
            connection.stop();
        }
    });
}

// Load views dynamically depending on User role
async function loadDashboard(user) {
    document.getElementById("auth-overlay").classList.add("hidden");
    document.getElementById("app-main").classList.remove("hidden");
    
    document.getElementById("session-username").innerText = user.username;
    
    const roleText = user.role;
    const roleBadge = document.getElementById("session-role");
    roleBadge.innerText = roleText;
    
    // Setup color badge
    roleBadge.className = "role-badge";
    if (roleText === "Admin") roleBadge.classList.add("admin");
    else if (roleText === "Dispatcher") roleBadge.classList.add("dispatcher");
    
    const panelAdmin = document.getElementById("panel-admin");
    const panelCitizen = document.getElementById("panel-citizen");
    const suggestionsContainer = document.getElementById("chat-suggestions-container");

    // Initialize Map first (Important to run before loading markers)
    if (!map) {
        initMap();
    }
    
    // Force Leaflet to recalculate container bounds and center on map coordinates
    setTimeout(() => {
        if (map) {
            map.invalidateSize();
            map.setView([40.9901, 29.0289], 14);
        }
    }, 200);
    const floatingWidgets = document.querySelector(".floating-widgets");
    if (floatingWidgets) floatingWidgets.classList.remove("hidden");

    // Show/hide view buttons depending on role
    const btnViewMap = document.getElementById("btn-view-map");
    const btnViewDepartments = document.getElementById("btn-view-departments");
    const btnAutopilot = document.getElementById("btn-autopilot");
    const mapDiv = document.getElementById("map");
    const departmentsDiv = document.getElementById("departments-view");

    if (roleText === "Admin" || roleText === "Dispatcher") {
        if (btnViewMap) btnViewMap.classList.remove("hidden");
        if (btnViewDepartments) btnViewDepartments.classList.remove("hidden");
        if (btnAutopilot) btnAutopilot.style.display = "flex";
        initAutopilot();
    } else {
        if (btnViewMap) {
            btnViewMap.classList.add("hidden");
            btnViewMap.classList.add("active");
        }
        if (btnViewDepartments) {
            btnViewDepartments.classList.add("hidden");
            btnViewDepartments.classList.remove("active");
        }
        if (btnAutopilot) btnAutopilot.style.display = "none";
        if (mapDiv) mapDiv.classList.remove("hidden");
        if (departmentsDiv) departmentsDiv.classList.add("hidden");
    }

    if (roleText === "Admin" || roleText === "Dispatcher") {
        panelAdmin.classList.remove("hidden");
        panelCitizen.classList.add("hidden");

        // Load admin suggestions
        suggestionsContainer.innerHTML = `
            <button class="suggest-btn" onclick="sendSuggestion('List status of all field crews')">Crew Status?</button>
            <button class="suggest-btn" onclick="sendSuggestion('Create a new road pothole incident report')">Report Pothole</button>
            <button class="suggest-btn" onclick="sendSuggestion('Generate a comprehensive system summary')">System Summary</button>
        `;

        document.getElementById("assistant-welcome").innerText = 
            `Hello Administrator ${user.username}! Welcome to CityPulse AI Operations Control. You can dispatch field crews or monitor real-time city metrics.`;

        // Enable map click reporting for admin/dispatcher as well
        map.off('click');
        map.on('click', onMapClick);
    } else {
        // Citizen view
        panelAdmin.classList.add("hidden");
        panelCitizen.classList.remove("hidden");

        // Load citizen suggestions
        suggestionsContainer.innerHTML = `
            <button class="suggest-btn" onclick="sendSuggestion('Sewer overflow reported on Main Avenue')">Sewer Overflow</button>
            <button class="suggest-btn" onclick="sendSuggestion('Fallen power cable on 5th Street')">Power Issue</button>
        `;

        document.getElementById("assistant-welcome").innerText = 
            `Hello Citizen ${user.username}! Welcome to CityPulse AI Assistant. You can chat with me to report urban issues or click directly on the map.`;

        // Load citizen reported incidents
        loadCitizenIncidents();

        // Enable citizen map click reporting
        map.off('click');
        map.on('click', onMapClick);
    }

    // Connect SignalR WebSocket and load data
    initSignalR();
    loadInitialData();
}

// 1. Map Initialization
function initMap() {
    const center = [40.9901, 29.0289];
    
    map = L.map('map', {
        zoomControl: false,
        attributionControl: false
    }).setView(center, 14);

    // Dark Mode Tile Layer (CartoDB Dark Matter)
    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
        maxZoom: 19
    }).addTo(map);

    // Re-position Zoom control to bottom-right
    L.control.zoom({
        position: 'bottomright'
    }).addTo(map);
}

// 2. Load Existing Data from Backend APIs
async function loadInitialData() {
    const user = checkSession();
    if (!user) return;

    if (user.role === "Admin" || user.role === "Dispatcher") {
        addLog("System", "Fetching spatial coordinates from database...");
    }
    
    try {
        // Fetch and draw incidents
        const incRes = await fetch("/api/incidents");
        const incidents = await incRes.json();
        incidents.forEach(addIncidentToMap);

        // Fetch and draw crews
        const crewRes = await fetch("/api/crews");
        const crews = await crewRes.json();
        crews.forEach(addCrewToMap);

        if (user.role === "Admin" || user.role === "Dispatcher") {
            // Update stats counters
            updateDashboardCounters();
            addLog("System", "City database synchronized successfully.");
        } else {
            loadCitizenIncidents();
        }
    } catch (err) {
        console.error("Error loading data:", err);
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Error", "Could not connect to API services.");
        }
    }
}

// 3. Connect to SignalR WebSocket Hub
async function initSignalR() {
    const statusDiv = document.getElementById("ws-status");
    const user = checkSession();
    if (!user) return;
    
    if (connection) {
        try {
            await connection.stop();
        } catch(e) {}
    }

    connection = new signalR.HubConnectionBuilder()
        .withUrl("/citypulsehub")
        .withAutomaticReconnect()
        .build();

    // SignalR Listeners
    connection.on("IncidentReported", (incident) => {
        addIncidentToMap(incident);
        
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Incident Reported", `New Incident: "${incident.title}" (Location: ${incident.lat.toFixed(4)}, ${incident.lng.toFixed(4)})`, "incident");
            showToast("New Incident", incident.title, "incident");
            updateDashboardCounters();
        } else {
            loadCitizenIncidents();
        }
        
        map.panTo([incident.lat, incident.lng]);
        checkAndRefreshDepartments();
    });

    connection.on("CrewAssigned", (data) => {
        updateCrewMarker(data.crewId, data.lat, data.lng, data.status);
        updateIncidentMarker(data.incidentId, "InProgress");

        // Draw a dashed route line from crew to incident
        drawAssignmentRoute(data.crewId, [data.lat, data.lng], [data.incidentLat, data.incidentLng]);
        
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Dispatch", `AI dispatched '${data.crewName}' crew to '${data.incidentTitle}'.`, "crew-assigned");
            updateDashboardCounters();
        }
        checkAndRefreshDepartments();
    });

    connection.on("CrewMoved", (data) => {
        updateCrewMarker(data.crewId, data.lat, data.lng, data.status);
        
        // Update route line as crew moves
        if (routeLines[data.crewId]) {
            const currentRoute = routeLines[data.crewId];
            const coordinates = currentRoute.getLatLngs();
            coordinates[0] = L.latLng(data.lat, data.lng);
            currentRoute.setLatLngs(coordinates);
        }

        // Live dispatch tracking update
        updateDispatchTracking(data);
    });

    connection.on("CrewArrived", (data) => {
        updateCrewMarker(data.crewId, null, null, "Busy");
        removeAssignmentRoute(data.crewId);

        // Remove from tracking panel
        removeDispatchTracking(data.crewId);

        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Arrived", `'${data.crewName}' crew arrived at '${data.incidentTitle}', work commenced.`, "crew-arrived");
            showToast("Crew Arrived", `${data.crewName} arrived at scene.`, "arrived");
            updateDashboardCounters();
        }
        checkAndRefreshDepartments();
    });

    connection.on("IncidentResolved", (data) => {
        removeAssignmentRoute(data.crewId);
        
        if (markers.incidents[data.incidentId]) {
            map.removeLayer(markers.incidents[data.incidentId]);
            delete markers.incidents[data.incidentId];
        }

        updateCrewMarker(data.crewId, null, null, "Idle");

        // Remove from tracking panel
        removeDispatchTracking(data.crewId);

        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Resolved", `"${data.incidentTitle}" has been resolved. Budget spent: $${data.budgetSpent}. Crew is now idle.`, "resolved");
            showToast("Issue Resolved", `"${data.incidentTitle}" resolved. Cost: $${data.budgetSpent.toLocaleString('en-US')}`, "resolved");
            updateDashboardCounters();
        } else {
            loadCitizenIncidents();
        }
        checkAndRefreshDepartments();
    });

    connection.on("CrewMembersUpdated", (data) => {
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Roster Update", `Crew personnel roster updated (Crew ID: ${data.crewId})`, "system");
        }
        checkAndRefreshDepartments();
    });

    connection.on("AutopilotToggled", (data) => {
        updateAutopilotUI(data.enabled);
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("AI Auto-Pilot", `AI Automated Dispatch Mode is now ${data.enabled ? 'ENABLED' : 'DISABLED'}.`, "system");
            showToast("Auto-Pilot Updated", `Automated Dispatch: ${data.enabled ? 'ON' : 'OFF'}`, "system");
        }
    });

    connection.on("BudgetWarning", (data) => {
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Budget Warning", `ALERT: ${data.departmentName} budget has exceeded 85% limit! (${(data.ratio * 100).toFixed(1)}%)`, "error");
            showToast("Budget Limit Warning", `${data.departmentName} budget usage is at ${(data.ratio * 100).toFixed(1)}%!`, "danger");
        }
        const cards = document.querySelectorAll(".department-card");
        cards.forEach(card => {
            const nameEl = card.querySelector(".dept-name");
            if (nameEl && nameEl.innerText.includes(data.departmentName)) {
                card.classList.add("budget-warning");
            }
        });
    });

    connection.on("BudgetTransferred", (data) => {
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Budget Transfer", `Transferred $${data.amount.toLocaleString('en-US')} from ${data.fromDepartmentName} to ${data.toDepartmentName}.`, "success");
            showToast("Budget Transferred", `$${data.amount.toLocaleString('en-US')} successfully transferred.`, "resolved");
        }
        checkAndRefreshDepartments();
    });

    // Connection Events
    connection.onreconnecting((error) => {
        statusDiv.className = "connection-status";
        statusDiv.querySelector(".status-text").innerText = "Reconnecting...";
        if (user.role === "Admin" || user.role === "Dispatcher") {
            addLog("Connection", "WebSocket disconnected, reconnecting...", "error");
        }
    });

    connection.onreconnected((connectionId) => {
        statusDiv.className = "connection-status connected";
        statusDiv.querySelector(".status-text").innerText = "Online";
    });

    connection.onclose((error) => {
        statusDiv.className = "connection-status";
        statusDiv.querySelector(".status-text").innerText = "Offline";
    });

    try {
        await connection.start();
        statusDiv.className = "connection-status connected";
        statusDiv.querySelector(".status-text").innerText = "Online";
    } catch (err) {
        console.error("SignalR Hub connection failed:", err);
        statusDiv.className = "connection-status";
        statusDiv.querySelector(".status-text").innerText = "Error";
    }
}

// 4. Map Drawing & Operations
function addIncidentToMap(incident) {
    if (markers.incidents[incident.id]) {
        map.removeLayer(markers.incidents[incident.id]);
    }

    const marker = L.marker([incident.lat, incident.lng], {
        icon: createIncidentIcon(incident.status)
    }).addTo(map);

    marker.incidentData = incident; // Store for dynamic updates

    const user = checkSession();
    const isAdminOrDispatcher = user && (user.role === "Admin" || user.role === "Dispatcher");
    let dispatchBtn = "";
    if (isAdminOrDispatcher && incident.status === "New") {
        dispatchBtn = `
            <button onclick="dispatchCrewDirectly(${incident.id})" class="popup-action-btn">
                <i class="fa-solid fa-truck-ramp-box"></i> Dispatch Nearest Crew
            </button>
        `;
    }

    const mediaHtml = getPopupMediaHtml(incident.imageUrl);
    const statusLabel = incident.status === 'InProgress' ? 'In Progress' : incident.status === 'New' ? 'New Incident' : 'Resolved';
    const popupContent = `
        <div class="popup-container">
            <h4>#${incident.id} - ${incident.title}</h4>
            <p>${incident.description}</p>
            ${mediaHtml}
            <span class="popup-badge badge-${incident.status.toLowerCase() == 'inprogress' ? 'working' : incident.status.toLowerCase()}">
                ${statusLabel}
            </span>
            ${dispatchBtn}
        </div>
    `;
    marker.bindPopup(popupContent);
    markers.incidents[incident.id] = marker;
}

function updateIncidentMarker(id, status) {
    const marker = markers.incidents[id];
    if (marker && marker.incidentData) {
        marker.incidentData.status = status;
        marker.setIcon(createIncidentIcon(status));

        const user = checkSession();
        const isAdminOrDispatcher = user && (user.role === "Admin" || user.role === "Dispatcher");
        let dispatchBtn = "";
        if (isAdminOrDispatcher && status === "New") {
            dispatchBtn = `
                <button onclick="dispatchCrewDirectly(${id})" class="popup-action-btn">
                    <i class="fa-solid fa-truck-ramp-box"></i> Dispatch Nearest Crew
                </button>
            `;
        }

        const mediaHtml = getPopupMediaHtml(marker.incidentData.imageUrl);
        const statusLabel = status === 'InProgress' ? 'In Progress' : status === 'New' ? 'New Incident' : 'Resolved';
        const popupContent = `
            <div class="popup-container">
                <h4>#${id} - ${marker.incidentData.title}</h4>
                <p>${marker.incidentData.description}</p>
                ${mediaHtml}
                <span class="popup-badge badge-${status.toLowerCase() == 'inprogress' ? 'working' : status.toLowerCase()}">
                    ${statusLabel}
                </span>
                ${dispatchBtn}
            </div>
        `;
        marker.setPopupContent(popupContent);
    }
}

function addCrewToMap(crew) {
    if (markers.crews[crew.id]) {
        map.removeLayer(markers.crews[crew.id]);
    }

    const marker = L.marker([crew.lat, crew.lng], {
        icon: createCrewIcon(crew.type, crew.status)
    }).addTo(map);

    const statusLabel = crew.status === 'OnWay' ? 'En Route' : crew.status === 'Busy' ? 'Busy' : 'Idle';
    const popupContent = `
        <div class="popup-container">
            <h4>${crew.name}</h4>
            <p>Type: <strong>${crew.type}</strong></p>
            <span class="popup-badge badge-${crew.status.toLowerCase() == 'onway' ? 'way' : crew.status.toLowerCase() == 'busy' ? 'working' : 'idle'}">
                Status: ${statusLabel}
            </span>
        </div>
    `;
    marker.bindPopup(popupContent);
    markers.crews[crew.id] = marker;
}

function updateCrewMarker(id, lat, lng, status) {
    const marker = markers.crews[id];
    if (marker) {
        if (lat && lng) {
            marker.setLatLng([lat, lng]);
        }
        
        let type = "Sanitation";
        const popup = marker.getPopup();
        if (popup) {
            const content = popup.getContent();
            if (content.includes("Zoning")) type = "Zoning";
            else if (content.includes("PublicWorks")) type = "PublicWorks";
            else if (content.includes("Parks")) type = "Parks";
        }

        marker.setIcon(createCrewIcon(type, status));

        const statusLabel = status === 'OnWay' ? 'En Route' : status === 'Busy' ? 'Busy' : 'Idle';
        const updatedPopupContent = `
            <div class="popup-container">
                <h4>Crew #${id}</h4>
                <p>Type: <strong>${type}</strong></p>
                <span class="popup-badge badge-${status.toLowerCase() == 'onway' ? 'way' : status.toLowerCase() == 'busy' ? 'working' : 'idle'}">
                    Status: ${statusLabel}
                </span>
            </div>
        `;
        marker.setPopupContent(updatedPopupContent);
    }
}

function drawAssignmentRoute(crewId, startLatLng, endLatLng) {
    removeAssignmentRoute(crewId);

    const line = L.polyline([startLatLng, endLatLng], {
        color: '#00f2fe',
        weight: 3,
        dashArray: '8, 8',
        opacity: 0.85
    }).addTo(map);

    routeLines[crewId] = line;
}

function removeAssignmentRoute(crewId) {
    if (routeLines[crewId]) {
        map.removeLayer(routeLines[crewId]);
        delete routeLines[crewId];
    }
}

// 5. Chat Bot Interaction
async function handleChatSubmit(e) {
    e.preventDefault();
    const input = document.getElementById("chat-input");
    const msg = input.value.trim();
    if (!msg && !chatAttachedFile) return;

    input.value = "";
    
    let chatLabel = msg;
    if (chatAttachedFile) {
        chatLabel = msg ? `${msg} (📎 Uploading Media)` : "📎 Uploading Media...";
    }
    appendMessage("user", "User", chatLabel);
    const typingId = appendTypingIndicator();

    const currentUser = checkSession();
    const currentUsername = currentUser ? currentUser.username : "Anonymous";

    try {
        let uploadedUrl = null;
        if (chatAttachedFile) {
            try {
                uploadedUrl = await uploadFile(chatAttachedFile);
            } catch (uploadErr) {
                removeTypingIndicator(typingId);
                appendMessage("system", "System Error", `Media could not be uploaded: ${uploadErr.message}`);
                return;
            }
        }

        const response = await fetch("/api/chat", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ message: msg, username: currentUsername, imageUrl: uploadedUrl })
        });
        
        const data = await response.json();
        removeTypingIndicator(typingId);
        appendMessage("system", "CityPulse AI", data.response);
        
        // Reset file state
        window.clearChatAttachment();

        const user = checkSession();
        if (user && (user.role === "Admin" || user.role === "Dispatcher")) {
            updateDashboardCounters();
        } else {
            loadCitizenIncidents();
        }
    } catch (err) {
        removeTypingIndicator(typingId);
        appendMessage("system", "System Error", "Could not communicate with AI services. Please check backend server.");
    }
}

async function loadDepartmentsData() {
    const grid = document.getElementById("departments-grid");
    if (!grid) return;

    grid.innerHTML = `<div class="empty-feed-text" style="grid-column: 1/-1; text-align: center; padding: 40px; font-size: 1.2rem;">Loading departments...</div>`;

    try {
        const res = await fetch("/api/departments");
        if (!res.ok) throw new Error("API error");
        const departments = await res.json();

        grid.innerHTML = "";

        departments.forEach(d => {
            const spentPercent = d.budgetLimit > 0 ? ((d.budgetSpent / d.budgetLimit) * 100).toFixed(1) : 0;
            const isWarning = spentPercent >= 85;

            const card = document.createElement("div");
            card.className = `department-card ${isWarning ? 'budget-warning' : ''}`;
            card.id = `dept-card-${d.id}`;

            let iconClass = "fa-building-shield";
            let typeColor = "#00f2fe";
            if (d.type === "Sanitation") { iconClass = "fa-trash-can"; typeColor = "#10b981"; }
            else if (d.type === "Zoning") { iconClass = "fa-compass-drafting"; typeColor = "#f59e0b"; }
            else if (d.type === "PublicWorks") { iconClass = "fa-road-barrier"; typeColor = "#00f2fe"; }
            else if (d.type === "Parks") { iconClass = "fa-tree"; typeColor = "#22c55e"; }

            card.innerHTML = `
                <div class="dept-header">
                    <div class="dept-icon-wrapper" style="background: rgba(${d.type === 'Sanitation' ? '16, 185, 129' : d.type === 'Zoning' ? '245, 158, 11' : d.type === 'PublicWorks' ? '0, 242, 254' : '34, 197, 94'}, 0.15); border-color: ${typeColor};">
                        <i class="fa-solid ${iconClass}" style="color: ${typeColor};"></i>
                    </div>
                    <div class="dept-title-area">
                        <div class="dept-name">${d.name}</div>
                        <div class="dept-type-badge">${d.type}</div>
                    </div>
                </div>

                <div class="dept-manager-info">
                    <span><i class="fa-solid fa-user-tie"></i> ${d.managerName}</span>
                    <span><i class="fa-solid fa-phone"></i> ${d.phoneNumber}</span>
                </div>

                <div class="dept-metrics-row">
                    <div class="dept-metric">
                        <span class="val">${d.activeIncidentsCount}</span>
                        <span class="lbl">Active</span>
                    </div>
                    <div class="dept-metric">
                        <span class="val">${d.resolvedIncidentsCount}</span>
                        <span class="lbl">Resolved</span>
                    </div>
                    <div class="dept-metric">
                        <span class="val">${d.activeCrewsCount}/${d.totalCrewsCount}</span>
                        <span class="lbl">Crews Active</span>
                    </div>
                </div>

                <div class="dept-budget-section">
                    <div class="budget-row">
                        <span class="lbl">Budget Limit:</span>
                        <span class="val">$${d.budgetLimit.toLocaleString('en-US')}</span>
                    </div>
                    <div class="budget-row">
                        <span class="lbl">Spent Budget:</span>
                        <span class="val ${isWarning ? 'text-danger' : ''}">$${d.budgetSpent.toLocaleString('en-US')} (${spentPercent}%)</span>
                    </div>
                    <div class="dept-budget-bar">
                        <div class="dept-budget-fill ${isWarning ? 'warning' : ''}" style="width: ${Math.min(spentPercent, 100)}%;"></div>
                    </div>
                    <div class="budget-row" style="margin-top: 6px; font-size: 0.8rem; color: var(--text-secondary);">
                        <span>Remaining:</span>
                        <span style="color: var(--text-primary); font-weight: 600;">$${d.budgetRemaining.toLocaleString('en-US')}</span>
                    </div>
                </div>

                <button class="dept-detail-btn" onclick="openDepartmentDetails(${d.id})">
                    <i class="fa-solid fa-arrow-up-right-from-square"></i> View Department Details
                </button>
            `;

            grid.appendChild(card);
        });

    } catch (err) {
        console.error("Error loading departments:", err);
        grid.innerHTML = `<div class="empty-feed-text" style="grid-column: 1/-1; color: var(--accent-red);">Could not load departments data.</div>`;
    }
}

async function openDepartmentDetails(deptId) {
    const modal = document.getElementById("dept-details-modal");
    const content = modal.querySelector(".dept-details-content");
    modal.classList.remove("hidden");
    content.innerHTML = `<div style="text-align: center; padding: 40px; color: var(--text-secondary);"><i class="fa-solid fa-spinner fa-spin fa-2x"></i><p style="margin-top: 15px;">Loading department details...</p></div>`;

    try {
        const [deptRes, archiveRes] = await Promise.all([
            fetch("/api/departments"),
            fetch(`/api/departments/${deptId}/archive`)
        ]);

        const departments = await deptRes.json();
        const archiveReports = await archiveRes.json();
        const d = departments.find(item => item.id === deptId);

        if (!d) throw new Error("Department not found");

        let avgResolutionTimeText = "N/A";
        if (archiveReports.length > 0) {
            let totalMs = 0;
            archiveReports.forEach(r => {
                const rep = new Date(r.reportedAt).getTime();
                const res = new Date(r.resolvedAt).getTime();
                totalMs += (res - rep);
            });
            const avgMs = totalMs / archiveReports.length;
            const avgMinutes = Math.round(avgMs / 60000);
            if (avgMinutes === 0) {
                const avgSeconds = Math.round(avgMs / 1000);
                avgResolutionTimeText = `${avgSeconds}s (Sim.)`;
            } else if (avgMinutes < 60) {
                avgResolutionTimeText = `${avgMinutes} min`;
            } else {
                const avgHours = (avgMinutes / 60).toFixed(1);
                avgResolutionTimeText = `${avgHours} hrs`;
            }
        }

        let avgCostText = "$0";
        if (archiveReports.length > 0) {
            const totalSpent = archiveReports.reduce((sum, r) => sum + r.budgetSpent, 0);
            avgCostText = `$${Math.round(totalSpent / archiveReports.length).toLocaleString('en-US')}`;
        }

        // Crew Stats
        const crewStats = {};
        d.crews.forEach(c => {
            crewStats[c.name] = 0;
        });
        archiveReports.forEach(r => {
            if (crewStats[r.resolvedByCrew] !== undefined) {
                crewStats[r.resolvedByCrew]++;
            } else {
                crewStats[r.resolvedByCrew] = 1;
            }
        });

        const maxResolved = Math.max(...Object.values(crewStats), 1);
        let chartHtml = "";
        Object.entries(crewStats).forEach(([crewName, count]) => {
            const widthPercent = (count / maxResolved) * 100;
            chartHtml += `
                <div style="margin-bottom: 12px;">
                    <div style="display:flex; justify-content:space-between; font-size:0.75rem; color:var(--text-secondary); margin-bottom:4px;">
                        <span>${crewName}</span>
                        <strong style="color:var(--text-primary);">${count} Jobs Completed</strong>
                    </div>
                    <div style="background: rgba(255,255,255,0.05); height: 8px; border-radius: 4px; overflow: hidden; border: 1px solid rgba(255,255,255,0.05);">
                        <div style="width: ${widthPercent}%; background: linear-gradient(90deg, #00f2fe, #4facfe); height: 100%; border-radius: 4px;"></div>
                    </div>
                </div>
            `;
        });

        // Archive table rows
        let archiveRows = "";
        if (archiveReports.length === 0) {
            archiveRows = `<tr><td colspan="6" style="text-align:center; padding: 25px; color: var(--text-secondary);">No resolved archived operations found for this department yet.</td></tr>`;
        } else {
            archiveReports.forEach(r => {
                const repDate = new Date(r.reportedAt).toLocaleDateString('en-US', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
                const resDate = new Date(r.resolvedAt).toLocaleDateString('en-US', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
                
                const mediaThumb = r.imageUrl ? `
                    <div style="display:inline-block; margin-right:6px; cursor:pointer;" onclick="openLightbox('${r.imageUrl}')">
                        <i class="fa-solid fa-image text-cyan" title="View Media"></i>
                    </div>
                ` : '';

                archiveRows += `
                    <tr>
                        <td style="font-weight:600; color:var(--text-primary);">#${r.id}</td>
                        <td>${mediaThumb}<strong>${r.incidentTitle}</strong></td>
                        <td><span class="role-badge" style="background:rgba(255,255,255,0.05); border:1px solid rgba(255,255,255,0.1); font-size:0.75rem;">${r.resolvedByCrew}</span></td>
                        <td style="color:#10b981; font-weight:600;">$${r.budgetSpent.toLocaleString('en-US')}</td>
                        <td style="font-size:0.75rem; color:var(--text-secondary);">${repDate} &rarr; ${resDate}</td>
                        <td>
                            <button onclick="downloadReportPdf(${r.id})" class="download-pdf-btn" title="Download Official PDF Report">
                                <i class="fa-solid fa-file-pdf"></i> Download PDF
                            </button>
                        </td>
                    </tr>
                `;
            });
        }

        // Crew Members Roster
        let crewsRosterHtml = "";
        d.crews.forEach(c => {
            const memberNames = c.members ? c.members.split(',').map(m => m.trim()).filter(Boolean) : [];
            const tags = memberNames.map(m => `
                <span class="crew-member-tag">
                    <i class="fa-solid fa-user" style="font-size:0.7rem;"></i> ${m}
                    <button onclick="removeCrewMember(${c.id}, '${m.replace(/'/g, "\\'")}')" title="Remove Member">&times;</button>
                </span>
            `).join('');

            crewsRosterHtml += `
                <div style="background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.05); border-radius: 8px; padding: 12px; margin-bottom: 10px;">
                    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 8px;">
                        <strong style="color:var(--text-primary); font-size: 0.85rem;"><i class="fa-solid fa-truck-pickup text-cyan"></i> ${c.name}</strong>
                        <span class="role-badge ${c.status === 'Idle' ? '' : 'dispatcher'}" style="font-size:0.7rem;">${c.status}</span>
                    </div>
                    <div style="display:flex; flex-wrap:wrap; gap:6px; margin-bottom: 8px;">
                        ${tags.length > 0 ? tags : '<span style="font-size:0.75rem; color:var(--text-secondary);">No assigned personnel.</span>'}
                    </div>
                    <div style="display:flex; gap:6px;">
                        <input type="text" id="add-member-input-${c.id}" placeholder="Staff name..." style="padding: 4px 8px; font-size: 0.75rem; background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.1); border-radius: 4px; color: #fff; flex: 1;">
                        <button onclick="addCrewMember(${c.id})" style="padding: 4px 10px; font-size: 0.75rem; background: rgba(0,242,254,0.15); border: 1px solid rgba(0,242,254,0.4); color: #00f2fe; border-radius: 4px; cursor: pointer;">
                            <i class="fa-solid fa-plus"></i> Add
                        </button>
                    </div>
                </div>
            `;
        });

        content.innerHTML = `
            <div class="dept-details-header">
                <div>
                    <h2><i class="fa-solid fa-building-shield text-cyan"></i> ${d.name}</h2>
                    <p style="color: var(--text-secondary); font-size: 0.85rem; margin-top: 4px;">
                        Manager: <strong>${d.managerName}</strong> &bull; Contact: <strong>${d.phoneNumber}</strong>
                    </p>
                </div>
                <button class="dept-details-close-btn" onclick="closeDepartmentDetails()"><i class="fa-solid fa-xmark"></i></button>
            </div>

            <!-- Department KPI Summary Row -->
            <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 15px; margin-bottom: 20px;">
                <div class="dept-stat-box">
                    <span class="lbl">Active Incidents</span>
                    <span class="val text-amber">${d.activeIncidentsCount}</span>
                </div>
                <div class="dept-stat-box">
                    <span class="lbl">Total Resolved</span>
                    <span class="val text-green">${d.resolvedIncidentsCount}</span>
                </div>
                <div class="dept-stat-box">
                    <span class="lbl">Avg Resolution Time</span>
                    <span class="val text-cyan">${avgResolutionTimeText}</span>
                </div>
                <div class="dept-stat-box">
                    <span class="lbl">Avg Repair Cost</span>
                    <span class="val text-purple">${avgCostText}</span>
                </div>
            </div>

            <!-- Two-column row: Crew performance & Staff roster -->
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-bottom: 25px;">
                <div style="background: rgba(10, 15, 29, 0.6); border: 1px solid rgba(255, 255, 255, 0.05); border-radius: 12px; padding: 18px;">
                    <h3 style="font-size: 0.95rem; margin-bottom: 15px; color: var(--text-primary);"><i class="fa-solid fa-chart-column text-cyan"></i> Crew Job Distribution</h3>
                    ${chartHtml}
                </div>

                <div style="background: rgba(10, 15, 29, 0.6); border: 1px solid rgba(255, 255, 255, 0.05); border-radius: 12px; padding: 18px;">
                    <h3 style="font-size: 0.95rem; margin-bottom: 15px; color: var(--text-primary);"><i class="fa-solid fa-users-gear text-purple"></i> Crew Rosters & Staffing</h3>
                    ${crewsRosterHtml}
                </div>
            </div>

            <!-- Archive Reports Table -->
            <div class="dept-archive-table-wrapper">
                <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 15px;">
                    <h3 style="font-size: 1rem; color: var(--text-primary); margin: 0;">
                        <i class="fa-solid fa-clock-rotate-left text-green"></i> Resolved Incidents & Operation Archive
                    </h3>
                    <span style="font-size: 0.8rem; color: var(--text-secondary);">${archiveReports.length} records found</span>
                </div>

                <table class="dept-archive-table">
                    <thead>
                        <tr>
                            <th>ID</th>
                            <th>Incident Title</th>
                            <th>Assigned Crew</th>
                            <th>Cost</th>
                            <th>Timeline</th>
                            <th>Official Document</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${archiveRows}
                    </tbody>
                </table>
            </div>
        `;

    } catch (err) {
        console.error("Error opening department details:", err);
        content.innerHTML = `<div style="text-align: center; padding: 40px; color: var(--accent-red);"><i class="fa-solid fa-triangle-exclamation fa-2x"></i><p style="margin-top: 15px;">Could not load department details.</p></div>`;
    }
}

function closeDepartmentDetails() {
    const modal = document.getElementById("dept-details-modal");
    if (modal) modal.classList.add("hidden");
}

window.downloadReportPdf = function(reportId) {
    if (window.photino && window.photino.sendMessage) {
        window.photino.sendMessage(JSON.stringify({ type: "open_pdf_local", id: reportId }));
    } else {
        window.open(`/api/reports/${reportId}/pdf?download=true`, '_blank');
    }
};

window.addCrewMember = async function(crewId) {
    const input = document.getElementById(`add-member-input-${crewId}`);
    if (!input || !input.value.trim()) return;
    const name = input.value.trim();

    try {
        const res = await fetch(`/api/departments/crews/${crewId}/members/add`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ name })
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || "Failed to add member");
        input.value = "";
        
        // Refresh modal
        const modal = document.getElementById("dept-details-modal");
        const titleEl = modal.querySelector(".dept-details-header h2");
        if (titleEl) {
            const deptsRes = await fetch("/api/departments");
            const depts = await deptsRes.json();
            const currentDept = depts.find(d => titleEl.innerText.includes(d.name));
            if (currentDept) openDepartmentDetails(currentDept.id);
        }
    } catch (err) {
        alert(err.message);
    }
};

window.removeCrewMember = async function(crewId, name) {
    if (!confirm(`Are you sure you want to remove '${name}' from this crew?`)) return;

    try {
        const res = await fetch(`/api/departments/crews/${crewId}/members/remove`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ name })
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || "Failed to remove member");

        // Refresh modal
        const modal = document.getElementById("dept-details-modal");
        const titleEl = modal.querySelector(".dept-details-header h2");
        if (titleEl) {
            const deptsRes = await fetch("/api/departments");
            const depts = await deptsRes.json();
            const currentDept = depts.find(d => titleEl.innerText.includes(d.name));
            if (currentDept) openDepartmentDetails(currentDept.id);
        }
    } catch (err) {
        alert(err.message);
    }
};

// Budget Transfer Modal Handlers
window.openBudgetTransfer = async function() {
    const modal = document.getElementById("budget-transfer-modal");
    const fromSelect = document.getElementById("transfer-from");
    const toSelect = document.getElementById("transfer-to");
    const errDiv = document.getElementById("transfer-error");
    errDiv.innerText = "";
    document.getElementById("transfer-amount").value = "";

    try {
        const res = await fetch("/api/departments");
        const depts = await res.json();

        fromSelect.innerHTML = depts.map(d => `<option value="${d.id}">${d.name} (Remaining: $${(d.budgetRemaining).toLocaleString('en-US')})</option>`).join('');
        toSelect.innerHTML = depts.map(d => `<option value="${d.id}">${d.name} (Limit: $${d.budgetLimit.toLocaleString('en-US')})</option>`).join('');

        if (depts.length > 1) {
            toSelect.selectedIndex = 1;
        }

        modal.classList.remove("hidden");
    } catch (err) {
        console.error("Error loading departments for transfer:", err);
    }
};

window.closeBudgetTransfer = function() {
    const modal = document.getElementById("budget-transfer-modal");
    if (modal) modal.classList.add("hidden");
};

document.getElementById("budget-transfer-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const fromDeptId = parseInt(document.getElementById("transfer-from").value);
    const toDeptId = parseInt(document.getElementById("transfer-to").value);
    const amount = parseFloat(document.getElementById("transfer-amount").value);
    const errDiv = document.getElementById("transfer-error");
    errDiv.innerText = "";

    if (fromDeptId === toDeptId) {
        errDiv.innerText = "Source and target departments cannot be the same.";
        return;
    }

    try {
        const res = await fetch("/api/departments/transfer-budget", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fromDepartmentId: fromDeptId, toDepartmentId: toDeptId, amount })
        });
        const data = await res.json();
        if (!res.ok) {
            errDiv.innerText = data.error || "Transfer failed.";
            return;
        }

        closeBudgetTransfer();
        loadDepartmentsData();
    } catch (err) {
        errDiv.innerText = "Error executing budget transfer.";
    }
});

// Autopilot Management
async function initAutopilot() {
    try {
        const res = await fetch("/api/settings/autopilot");
        const data = await res.json();
        updateAutopilotUI(data.enabled);
    } catch(e) {}
}

window.toggleAutopilot = async function() {
    const lbl = document.getElementById("autopilot-status-lbl");
    const currentlyEnabled = lbl.innerText === "ON";
    try {
        const res = await fetch("/api/settings/autopilot", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ enabled: !currentlyEnabled })
        });
        const data = await res.json();
        updateAutopilotUI(data.enabled);
    } catch(e) {}
};

function updateAutopilotUI(enabled) {
    const btn = document.getElementById("btn-autopilot");
    const lbl = document.getElementById("autopilot-status-lbl");
    if (!btn || !lbl) return;

    if (enabled) {
        lbl.innerText = "ON";
        btn.classList.add("autopilot-active");
    } else {
        lbl.innerText = "OFF";
        btn.classList.remove("autopilot-active");
    }
}

// Live Dispatch Tracking Widget
function updateDispatchTracking(data) {
    const panel = document.getElementById("dispatch-tracking-panel");
    const container = document.getElementById("dispatch-tracking-container");
    if (!panel || !container) return;

    if (data.status === "OnWay" && data.targetIncidentTitle) {
        panel.style.display = "block";
        let card = document.getElementById(`tracking-crew-${data.crewId}`);
        const distText = data.distanceMeters !== null ? `${data.distanceMeters} m` : "En Route";

        if (!card) {
            card = document.createElement("div");
            card.id = `tracking-crew-${data.crewId}`;
            card.className = "dispatch-track-card";
            container.appendChild(card);
        }

        card.innerHTML = `
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                <span style="font-weight: 600; color: var(--text-primary); font-size: 0.8rem;">
                    <i class="fa-solid fa-truck-fast text-cyan"></i> ${data.crewName}
                </span>
                <span class="role-badge dispatcher" style="font-size: 0.65rem; padding: 2px 6px;">
                    ${distText}
                </span>
            </div>
            <div style="font-size: 0.75rem; color: var(--text-secondary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
                Destination: <strong style="color: var(--text-primary);">${data.targetIncidentTitle}</strong>
            </div>
        `;
    } else {
        removeDispatchTracking(data.crewId);
    }
}

function removeDispatchTracking(crewId) {
    const card = document.getElementById(`tracking-crew-${crewId}`);
    if (card) card.remove();

    const container = document.getElementById("dispatch-tracking-container");
    const panel = document.getElementById("dispatch-tracking-panel");
    if (container && container.children.length === 0 && panel) {
        panel.style.display = "none";
    }
}

// Helper to check and refresh departments if view is open
function checkAndRefreshDepartments() {
    const deptView = document.getElementById("departments-view");
    if (deptView && !deptView.classList.contains("hidden")) {
        loadDepartmentsData();
    }
}

// Map Click Incident Creation
function onMapClick(e) {
    const { lat, lng } = e.latlng;
    const user = checkSession();
    const reportedBy = user ? user.username : "Anonymous";

    const popupContent = `
        <div class="popup-container" style="min-width: 240px;">
            <h4><i class="fa-solid fa-map-pin text-cyan"></i> Report Incident Here</h4>
            <p style="font-size:0.75rem; color:var(--text-secondary);">Lat: ${lat.toFixed(4)}, Lng: ${lng.toFixed(4)}</p>
            <form id="map-report-form" style="display: flex; flex-direction: column; gap: 8px; margin-top: 8px;">
                <input type="text" id="map-inc-title" placeholder="Title (e.g. Sewer Overflow)..." required style="padding: 6px; font-size: 0.8rem; background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.1); border-radius: 4px; color: #fff;">
                <textarea id="map-inc-desc" placeholder="Details..." rows="2" style="padding: 6px; font-size: 0.8rem; background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.1); border-radius: 4px; color: #fff; resize: vertical;"></textarea>
                <div style="display:flex; gap:6px;">
                    <label style="flex:1; cursor:pointer; padding:6px; background:rgba(255,255,255,0.05); border:1px dashed rgba(255,255,255,0.2); border-radius:4px; text-align:center; font-size:0.75rem; color:var(--text-secondary);">
                        <i class="fa-solid fa-camera"></i> Media
                        <input type="file" id="map-inc-file" accept="image/*,video/*" style="display:none;" onchange="this.parentElement.style.borderColor='#00f2fe'; this.parentElement.style.color='#00f2fe';">
                    </label>
                </div>
                <div style="display: flex; gap: 6px; margin-top: 4px;">
                    <button type="submit" class="popup-action-btn" style="flex: 1; margin: 0;">Submit</button>
                    <button type="button" onclick="map.closePopup()" style="padding: 6px 10px; font-size: 0.75rem; background: transparent; border: 1px solid rgba(255,255,255,0.2); border-radius: 4px; color: #fff; cursor: pointer;">Cancel</button>
                </div>
            </form>
        </div>
    `;

    L.popup()
        .setLatLng([lat, lng])
        .setContent(popupContent)
        .openOn(map);

    setTimeout(() => {
        const form = document.getElementById("map-report-form");
        if (form) {
            form.addEventListener("submit", async (ev) => {
                ev.preventDefault();
                const title = document.getElementById("map-inc-title").value.trim();
                const desc = document.getElementById("map-inc-desc").value.trim();
                const fileInput = document.getElementById("map-inc-file");
                
                let imageUrl = null;
                if (fileInput && fileInput.files.length > 0) {
                    try {
                        imageUrl = await uploadFile(fileInput.files[0]);
                    } catch (err) {
                        alert("Could not upload media file.");
                    }
                }

                try {
                    const res = await fetch("/api/incidents", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({
                            title,
                            description: desc,
                            lat,
                            lng,
                            reportedBy,
                            imageUrl
                        })
                    });
                    
                    if (res.ok) {
                        map.closePopup();
                    } else {
                        alert("Could not create incident.");
                    }
                } catch (err) {
                    console.error("Error creating incident:", err);
                }
            });
        }
    }, 100);
}

// Direct crew dispatch button click from popup
window.dispatchCrewDirectly = async function(incidentId) {
    try {
        const res = await fetch(`/api/incidents/${incidentId}/assign`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({})
        });
        const data = await res.json();
        if (!res.ok) {
            alert(data.error || "Failed to dispatch crew.");
        } else {
            map.closePopup();
        }
    } catch (err) {
        console.error("Error dispatching crew:", err);
    }
};

// Citizen Reported Incidents List
async function loadCitizenIncidents() {
    const list = document.getElementById("citizen-incidents-list");
    if (!list) return;

    try {
        const res = await fetch("/api/incidents");
        const incidents = await res.json();

        if (incidents.length === 0) {
            list.innerHTML = `<div class="empty-feed-text">You have not reported any incidents yet.</div>`;
            return;
        }

        list.innerHTML = incidents.map(i => {
            const timeStr = new Date(i.createdAt).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
            const mediaThumb = i.imageUrl ? `
                <div class="citizen-inc-thumb" onclick="openLightbox('${i.imageUrl}')">
                    <i class="fa-solid fa-image"></i>
                </div>
            ` : '';
            return `
                <div class="citizen-inc-card" onclick="map.setView([${i.lat}, ${i.lng}], 16); markers.incidents[${i.id}]?.openPopup();">
                    <div style="display:flex; justify-content:space-between; align-items:flex-start;">
                        <strong style="color:var(--text-primary); font-size:0.85rem;">${i.title}</strong>
                        <span class="popup-badge badge-${i.status.toLowerCase() == 'inprogress' ? 'working' : i.status.toLowerCase()}" style="font-size:0.65rem;">
                            ${i.status === 'InProgress' ? 'In Progress' : i.status === 'New' ? 'New' : 'Resolved'}
                        </span>
                    </div>
                    <p style="font-size:0.75rem; color:var(--text-secondary); margin: 4px 0;">${i.description || 'No description'}</p>
                    <div style="display:flex; justify-content:space-between; align-items:center; margin-top:6px;">
                        ${mediaThumb}
                        <span style="font-size:0.7rem; color:var(--text-secondary);"><i class="fa-regular fa-clock"></i> ${timeStr}</span>
                    </div>
                </div>
            `;
        }).join('');
    } catch(e) {}
}

// Update KPI Stats
async function updateDashboardCounters() {
    try {
        const res = await fetch("/api/system/metrics");
        if (!res.ok) return;
        const data = await res.json();

        const activeEl = document.getElementById("stat-active-incidents");
        const idleEl = document.getElementById("stat-idle-crews");
        const resolvedEl = document.getElementById("stat-resolved-incidents");
        const budgetEl = document.getElementById("stat-budget");

        if (activeEl) activeEl.innerText = data.activeIncidents;
        if (idleEl) idleEl.innerText = data.idleCrews;
        if (resolvedEl) resolvedEl.innerText = data.resolvedIncidents;
        if (budgetEl) budgetEl.innerText = `$${(data.totalBudgetSpent || 0).toLocaleString('en-US')}`;
    } catch(e) {}
}

// Add Entry to Operations Log Feed
function addLog(source, message, type = "system") {
    const container = document.getElementById("logs-container");
    if (!container) return;

    const entry = document.createElement("div");
    entry.className = `log-entry ${type}`;
    
    const time = new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    entry.innerHTML = `
        <span class="log-time">${time} &bull; ${source}</span>
        <span class="log-text">${message}</span>
    `;

    container.insertBefore(entry, container.firstChild);

    // Limit log entries to 50
    while (container.children.length > 50) {
        container.removeChild(container.lastChild);
    }
}

// Toast Notifications
function showToast(title, message, type = "info") {
    const container = document.getElementById("toast-container");
    if (!container) return;

    const toast = document.createElement("div");
    toast.className = `toast-item toast-${type}`;

    let icon = "fa-info-circle";
    if (type === "incident") icon = "fa-triangle-exclamation";
    else if (type === "arrived") icon = "fa-truck";
    else if (type === "resolved") icon = "fa-circle-check";
    else if (type === "danger") icon = "fa-bell";

    toast.innerHTML = `
        <i class="fa-solid ${icon} toast-icon"></i>
        <div class="toast-body">
            <strong>${title}</strong>
            <p>${message}</p>
        </div>
    `;

    container.appendChild(toast);

    setTimeout(() => {
        toast.classList.add("fade-out");
        setTimeout(() => toast.remove(), 400);
    }, 4500);
}

// Chat UI Controls
function toggleChatSidebar(open) {
    const container = document.getElementById("app-main");
    if (!container) return;
    if (open) {
        container.classList.remove("chat-collapsed");
    } else {
        container.classList.add("chat-collapsed");
    }
}

function appendMessage(sender, senderName, text) {
    const container = document.getElementById("chat-messages");
    if (!container) return;

    const msgDiv = document.createElement("div");
    msgDiv.className = `message ${sender}`;

    let avatarIcon = sender === "user" ? "fa-user" : "fa-robot";
    const timeStr = new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });

    msgDiv.innerHTML = `
        <div class="avatar"><i class="fa-solid ${avatarIcon}"></i></div>
        <div class="message-content">
            <strong>${senderName}</strong>
            <p>${text.replace(/\n/g, '<br>')}</p>
            <span class="time">${timeStr}</span>
        </div>
    `;

    container.appendChild(msgDiv);
    container.scrollTop = container.scrollHeight;
}

function appendTypingIndicator() {
    const container = document.getElementById("chat-messages");
    if (!container) return null;

    const id = "typing-" + Date.now();
    const typingDiv = document.createElement("div");
    typingDiv.id = id;
    typingDiv.className = "message system typing";
    typingDiv.innerHTML = `
        <div class="avatar"><i class="fa-solid fa-robot"></i></div>
        <div class="message-content">
            <div class="typing-dots"><span></span><span></span><span></span></div>
        </div>
    `;
    container.appendChild(typingDiv);
    container.scrollTop = container.scrollHeight;
    return id;
}

function removeTypingIndicator(id) {
    if (!id) return;
    const el = document.getElementById(id);
    if (el) el.remove();
}

window.sendSuggestion = function(text) {
    const input = document.getElementById("chat-input");
    if (input) {
        input.value = text;
        const form = document.getElementById("chat-form");
        if (form) form.dispatchEvent(new Event("submit"));
    }
};

// Chat attachment handling
document.getElementById("chat-attach-btn")?.addEventListener("click", () => {
    document.getElementById("chat-file-input")?.click();
});

document.getElementById("chat-file-input")?.addEventListener("change", (e) => {
    if (e.target.files.length > 0) {
        chatAttachedFile = e.target.files[0];
        const preview = document.getElementById("chat-attachment-preview");
        if (preview) {
            preview.classList.remove("hidden");
            preview.innerHTML = `
                <span><i class="fa-solid fa-file"></i> ${chatAttachedFile.name}</span>
                <button type="button" onclick="clearChatAttachment()">&times;</button>
            `;
        }
    }
});

document.getElementById("chat-form")?.addEventListener("submit", handleChatSubmit);

// View Switching: Map vs Departments
document.getElementById("btn-view-map")?.addEventListener("click", () => {
    document.getElementById("btn-view-map").classList.add("active");
    document.getElementById("btn-view-departments").classList.remove("active");
    document.getElementById("map").classList.remove("hidden");
    document.getElementById("departments-view").classList.add("hidden");
    const headerDesc = document.getElementById("header-desc");
    if (headerDesc) headerDesc.innerText = "Real-Time Spatial GIS Tracking and AI Dispatch Orchestration";
    if (map) map.invalidateSize();
});

document.getElementById("btn-view-departments")?.addEventListener("click", () => {
    document.getElementById("btn-view-departments").classList.add("active");
    document.getElementById("btn-view-map").classList.remove("active");
    document.getElementById("map").classList.add("hidden");
    document.getElementById("departments-view").classList.remove("hidden");
    const headerDesc = document.getElementById("header-desc");
    if (headerDesc) headerDesc.innerText = "Municipal Departments, Budgets and Operational Analytics";
    loadDepartmentsData();
});

// Refresh Data Button
document.getElementById("btn-reset-data")?.addEventListener("click", () => {
    loadInitialData();
    checkAndRefreshDepartments();
    showToast("Data Refreshed", "Live GIS coordinates and metrics synchronized.", "info");
});

// Right Sidebar Toggle Button
document.getElementById("sidebar-toggle-btn")?.addEventListener("click", () => {
    const widgets = document.querySelector(".floating-widgets");
    if (widgets) {
        widgets.classList.toggle("collapsed");
    }
});
