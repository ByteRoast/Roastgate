FROM roastgate:test
RUN apt-get update && apt-get install -y --no-install-recommends nginx libnginx-mod-http-fancyindex openssl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /etc/nginx/whitelists /var/www/certbot
# A stand-in for certbot (see fake-certbot.sh): it keeps the command line and the file layout but cannot reach Let's Encrypt.
COPY fake-certbot.sh /usr/local/bin/certbot
RUN chmod +x /usr/local/bin/certbot && mkdir -p /etc/letsencrypt
ENV ROASTGATE_HOST_EXEC=direct
# Real nginx in the same container, so `nginx -t` and `nginx -s reload` are the real thing.
ENTRYPOINT ["sh", "-c", "nginx && exec /app/Roastgate.Web"]
