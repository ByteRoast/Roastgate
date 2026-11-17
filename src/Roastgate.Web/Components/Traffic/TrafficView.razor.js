const projection = d3.geoNaturalEarth1().scale(182.79).translate([500, 252.67]);

export async function init(container, svgUrl) {
    container.innerHTML = await (await fetch(svgUrl)).text();

    const svg = d3.select(container).select("svg");

    // Everything that moves with the zoom goes into one group: the countries, then the points on top.
    const world = svg.append("g");
    world.node().append(...svg.selectAll(":scope > path").nodes());

    const countries = world.selectAll(":scope > path");
    const pointsLayer = world.append("g");

    let points = pointsLayer.selectAll("circle");
    let radius = () => 0;
    let scale = 1;

    // Filled by update(); the hover handlers read it.
    let byCountry = new Map();

    const format = number => number.toLocaleString("en-US");
    const tooltip = d3.select(container).append("div").attr("class", "traffic-tooltip");

    function showTooltip(event, countryCode, title, details) {
        tooltip.selectAll("*").remove();

        const heading = tooltip.append("div").attr("class", "traffic-tooltip-title");

        if (countryCode) {
            heading.append("span").attr("class", `fi fi-${countryCode.toLowerCase()} traffic-flag`);
        }

        // .text() and not .html(): city names come from the database, never use them as markup.
        heading.append("span").text(title);
        tooltip.append("div").text(details);

        moveTooltip(event);
        tooltip.classed("traffic-tooltip--visible", true);
    }

    function moveTooltip(event) {
        const [x, y] = d3.pointer(event, container);
        tooltip.style("left", `${x}px`).style("top", `${y}px`);
    }

    function hideTooltip() {
        tooltip.classed("traffic-tooltip--visible", false);
    }

    countries
        .on("pointerenter", event => {
            const code = event.currentTarget.dataset.country;
            const country = byCountry.get(code);

            if (country) {
                showTooltip(event, code, country.name, `${format(country.requests)} requests`);
            }
        })
        .on("pointermove", moveTooltip)
        .on("pointerleave", hideTooltip);

    const zoom = d3.zoom()
        .scaleExtent([1, 12])
        .translateExtent([[0, 0], [1000, 435]])
        .on("zoom", event => {
            hideTooltip();

            scale = event.transform.k;
            world.attr("transform", event.transform);

            // Divided by the zoom level, so that the circles keep their size on screen.
            points.attr("r", point => radius(point) / scale).attr("stroke-width", 1 / scale);
        });

    svg.call(zoom);

    return {
        update(data) {
            // Requests per country, from the data-country attribute of each path of world.svg.
            byCountry = d3.rollup(
                data.filter(point => point.countryCode),
                rows => ({ name: rows[0].country, requests: d3.sum(rows, row => row.requests) }),
                point => point.countryCode,
            );

            const maxRequests = d3.max(byCountry.values(), country => country.requests) ?? 1;

            // Logarithmic: one country with thousands of requests must not wash out all the others.
            const shade = d3.scaleLog()
                .domain([1, Math.max(maxRequests, 2)])
                .range([0.2, 1])
                .clamp(true);

            // A transition needs a real color, not var(--mud-palette-primary).
            const primary = getComputedStyle(container).getPropertyValue("--mud-palette-primary").trim();

            countries
                .transition()
                .duration(400)
                .style("fill", function () {
                    return byCountry.has(this.dataset.country) ? primary : null;
                })
                .style("fill-opacity", function () {
                    const country = byCountry.get(this.dataset.country);
                    return country ? shade(country.requests) : null;
                });
            
            const located = data.filter(point => projection([point.longitude, point.latitude]));
            const max = d3.max(located, point => point.requests) ?? 1;

            radius = point => 1.5 + 6 * Math.sqrt(point.requests / max);

            points = pointsLayer
                .selectAll("circle")
                .data(located, point => `${point.latitude},${point.longitude}`)
                .join("circle")
                .attr("class", "traffic-point")
                .attr("transform", point => `translate(${projection([point.longitude, point.latitude])})`)
                .attr("r", point => radius(point) / scale)
                .attr("stroke-width", 1 / scale)
                .on("pointerenter", (event, point) => showTooltip(
                    event,
                    point.countryCode,
                    point.city ?? point.country ?? "Unknown place",
                    `${format(point.requests)} requests · ${format(point.addresses)} addresses`,
                ))
                .on("pointermove", moveTooltip)
                .on("pointerleave", hideTooltip);
        },

        zoomBy(factor) {
            svg.transition().duration(300).call(zoom.scaleBy, factor);
        },

        reset() {
            svg.transition().duration(300).call(zoom.transform, d3.zoomIdentity);
        },
    };
}
