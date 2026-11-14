import { execSync } from 'node:child_process';

import { CONTAINER } from './config.ts';

export const httpGet = (port: number, host: string, path = '/'): string => {
  return sh(
    `bash -c 'exec 3<>/dev/tcp/127.0.0.1/${port}; printf "GET ${path} HTTP/1.0\\r\\nHost: ${host}\\r\\n\\r\\n" >&3; cat <&3'`,
  );
};

export const readSetupToken = () => {
  const logs = execSync(`docker logs ${CONTAINER} 2>&1`, { encoding: 'utf8' });

  return /setup token: (\w+)/.exec(logs)?.[1];
};

export const requestFromLocalhost = (port: number): string => {
  return sh(
    `bash -c 'exec 3<>/dev/tcp/127.0.0.1/${port}; printf "GET / HTTP/1.0\\r\\n\\r\\n" >&3; head -1 <&3'`,
  );
};

export const sh = (command: string): string => {
  try {
    return execSync(
      `docker exec ${CONTAINER} sh -c ${JSON.stringify(command)}`,
      {
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'pipe'],
      },
    ).trim();
  } catch (e) {
    // execSync throws an Error carrying the exit status and the output of the command.
    const failure = e as {
      status?: number;
      stderr?: Buffer | string;
      stdout?: Buffer | string;
    };

    return `ERR ${failure.status}: ${(failure.stderr || failure.stdout || '').toString().trim()}`;
  }
};

// Writes a file in the container with its real newlines (printf through two shells would keep the backslashes).
export const writeHostFile = (path: string, content: string): string => {
  return sh(
    `bash -c 'echo ${Buffer.from(content).toString('base64')} | base64 -d > ${path}'`,
  );
};
