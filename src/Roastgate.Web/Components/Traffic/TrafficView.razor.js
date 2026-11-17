const projection = d3.geoNaturalEarth1().scale(182.79).translate([500, 252.67]);

export async function init(container, svgUrl) {
    container.innerHTML = await (await fetch(svgUrl)).text();

    const svg = d3.select(container).select("svg");

    // Everything that moves with the zoom goes into one group: the countries, then the points on top.
    const world = svg.append("g");
    world.node().append(...svg.selectAll(":scope > path").nodes());

    const pointsLayer = world.append("g");

    let points = pointsLayer.selectAll("circle");
    let radius = () => 0;
    let scale = 1;

    const zoom = d3.zoom()
        .scaleExtent([1, 12])
        .translateExtent([[0, 0], [1000, 435]])
        .on("zoom", event => {
            scale = event.transform.k;
            world.attr("transform", event.transform);
            // Divided by the zoom level, so that the circles keep their size on screen.
            points.attr("r", point => radius(point) / scale).attr("stroke-width", 1 / scale);
        });

    svg.call(zoom);

    return {
        update(data) {
            const located = data.filter(point => projection([point.longitude, point.latitude]));
            const max = d3.max(located, point => point.requests) ?? 1;

            radius = point => 2 + 12 * Math.sqrt(point.requests / max);

            points = pointsLayer
                .selectAll("circle")
                .data(located, point => `${point.latitude},${point.longitude}`)
                .join("circle")
                .attr("class", "traffic-point")
                .attr("transform", point => `translate(${projection([point.longitude, point.latitude])})`)
                .attr("r", point => radius(point) / scale)
                .attr("stroke-width", 1 / scale);

            points.selectAll("title")
                .data(point => [point])
                .join("title")
                .text(point => `${point.city ?? "?"}, ${point.country ?? "?"}: ${point.requests} requests, ${point.addresses} addresses`);
        },

        zoomBy(factor) {
            svg.transition().duration(300).call(zoom.scaleBy, factor);
        },

        reset() {
            svg.transition().duration(300).call(zoom.transform, d3.zoomIdentity);
        },
    };
}
