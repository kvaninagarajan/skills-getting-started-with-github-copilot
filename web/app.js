const status = document.getElementById("status");
let locations = [];
const map = typeof L !== "undefined" ? L.map("map").setView([20, 0], 2) : null;
const markers = map ? L.layerGroup().addTo(map) : null;
if (map) {
  L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  }).addTo(map);
} else {
  document.getElementById("map").textContent = "Map library unavailable. Locations are still listed below.";
}

const labels = {
  located: "Located",
  notFound: "Address not found",
  notGeocoded: "Coordinates missing (geocoding not requested)",
  geocodingUnavailable: "Geocoder unavailable or not configured"
};

function render() {
  const query = document.getElementById("search").value.toLowerCase();
  const visible = locations.filter(x => `${x.addressId} ${x.address}`.toLowerCase().includes(query));
  const table = document.getElementById("addresses");
  table.replaceChildren();
  markers?.clearLayers();
  const bounds = [];
  for (const location of visible) {
    const row = document.createElement("tr");
    for (const text of [location.addressId, location.address, labels[location.status] || location.status]) {
      const cell = document.createElement("td");
      cell.textContent = text;
      row.append(cell);
    }
    const cell = document.createElement("td");
    if (location.latitude !== null && location.longitude !== null) {
      const point = [location.latitude, location.longitude];
      cell.textContent = `${point[0]}, ${point[1]} `;
      if (map) {
        const popup = document.createElement("div");
        const id = document.createElement("strong");
        id.textContent = location.addressId;
        const address = document.createElement("p");
        address.textContent = location.address;
        popup.append(id, address);
        const marker = L.marker(point).bindPopup(popup).addTo(markers);
        const button = document.createElement("button");
        button.textContent = "Show on map";
        button.addEventListener("click", () => {
          map.setView(point, 16);
          marker.openPopup();
        });
        cell.append(button);
      }
      bounds.push(point);
    } else {
      cell.textContent = "—";
    }
    row.append(cell);
    table.append(row);
  }
  if (map && bounds.length) map.fitBounds(bounds, { padding: [30, 30], maxZoom: 15 });
}

async function load() {
  const response = await fetch("/api/addresses");
  if (!response.ok) throw new Error("Could not load saved addresses.");
  locations = await response.json();
  render();
}

document.getElementById("search").addEventListener("input", render);
document.getElementById("upload").addEventListener("submit", async event => {
  event.preventDefault();
  const button = document.getElementById("submit");
  button.disabled = true;
  status.textContent = "Importing addresses… Please wait.";
  try {
    const data = new FormData(event.target);
    data.set("geocode", document.getElementById("geocode").checked);
    const response = await fetch("/api/addresses/import", { method: "POST", body: data });
    if (response.status === 413) throw new Error("The upload is too large.");
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || "Upload failed.");
    await load();
    status.textContent = `Imported ${result.imported} addresses; ${result.located} located. See statuses for unresolved addresses.`;
  } catch (error) {
    status.textContent = error.message;
  } finally {
    button.disabled = false;
  }
});
load().catch(error => { status.textContent = error.message; });
