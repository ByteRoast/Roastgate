const projection = d3.geoNaturalEarth1().scale(182.79).translate([500, 252.67]);

const svgNamespace = "http://www.w3.org/2000/svg";

export async function render(container, svgUrl, points) {
    const response = await fetch(svgUrl);

    container.innerHTML = await response.text();

    const svg = container.querySelector("svg");
    const layer = document.createElementNS(svgNamespace, "g");
    const max = Math.max(1, ...points.map(point => point.requests));

    for (const point of points) {
        const position = projection([point.longitude, point.latitude]);

        if (!position) {
            continue;
        }

        const circle = document.createElementNS(svgNamespace, "circle");
        circle.setAttribute("class", "traffic-point");
        circle.setAttribute("cx", position[0]);
        circle.setAttribute("cy", position[1]);
        // The area, not the radius, follows the requests: from 2 to 20 px.
        circle.setAttribute("r", 2 + 18 * Math.sqrt(point.requests / max));

        const title = document.createElementNS(svgNamespace, "title");
        title.textContent = `${point.city ?? "?"}, ${point.country ?? "?"}: ${point.requests} requests, ${point.addresses} addresses`;

        circle.append(title);
        layer.append(circle);
    }

    svg.append(layer);
}
