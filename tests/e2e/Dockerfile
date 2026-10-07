FROM roastgate:test
RUN apt-get update && apt-get install -y --no-install-recommends nginx && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /etc/nginx/whitelists /var/www/certbot
ENV ROASTGATE_HOST_EXEC=direct
# Real nginx in the same container, so `nginx -t` and `nginx -s reload` are the real thing.
ENTRYPOINT ["sh", "-c", "nginx && exec /app/Roastgate.Web"]
