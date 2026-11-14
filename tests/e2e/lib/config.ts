export const BASE: string = process.env.E2E_BASE ?? 'http://127.0.0.1:8199';
export const CHANNEL: string = process.env.E2E_CHANNEL ?? 'msedge';
export const CONTAINER: string = process.env.E2E_CONTAINER ?? 'roastgate-e2e';
export const OUT: string = process.env.E2E_OUT ?? './out';
export const SHOTS: string = `${OUT}/screenshots`;
