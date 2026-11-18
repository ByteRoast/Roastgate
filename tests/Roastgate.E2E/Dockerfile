FROM roastgate:test

RUN apt-get update \
    && apt-get install -y --no-install-recommends nginx libnginx-mod-http-fancyindex openssl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /etc/nginx/whitelists /var/www/certbot
    
# A stand-in for certbot (see fake-certbot.sh): it keeps the command line and the file layout but cannot reach Let's
# Encrypt.
COPY scripts/fake-certbot.sh /usr/local/bin/certbot

RUN chmod +x /usr/local/bin/certbot && mkdir -p /etc/letsencrypt

# MaxMind's public test database, under the name of the DB-IP one, so the traffic section can place a few addresses
# (81.2.69.142 is in London) without downloading 60 MB. `e2e:build` passes its folder as the `geoip` build context.
# /data is a VOLUME of the base image: BuildKit keeps what is copied into it, and `docker run` starts from it.
COPY --from=geoip GeoIP2-City-Test.mmdb /data/dbip-city-lite.mmdb

ENV ROASTGATE_HOST_EXEC=direct

# Real nginx in the same container, so `nginx -t` and `nginx -s reload` are the real thing.
ENTRYPOINT ["sh", "-c", "nginx && exec /app/Roastgate.Web"]
