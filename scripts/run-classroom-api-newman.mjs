import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const collectionPath = path.join(root, 'tests/api/classroom/Classroom API.postman_collection.json');
const environmentPath = path.join(root, 'tests/api/classroom/Classroom API.postman_environment.json');
const reportDirectory = path.join(root, 'TestResults/newman');
const jsonReport = path.join(reportDirectory, 'classroom-api.json');
const junitReport = path.join(reportDirectory, 'classroom-api.junit.xml');

function parseLocalEnvironment() {
  const file = path.join(root, '.env');
  let contents;
  try {
    contents = readFileSync(file, 'utf8');
  } catch {
    return {};
  }

  return Object.fromEntries(contents.split(/\r?\n/).flatMap(line => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) return [];
    const separator = trimmed.indexOf('=');
    if (separator < 1) return [];
    const key = trimmed.slice(0, separator).trim();
    let value = trimmed.slice(separator + 1).trim();
    if (value.length >= 2 && ['"', "'"].includes(value[0]) && value.at(-1) === value[0])
      value = value.slice(1, -1);
    return [[key, value]];
  }));
}

function setEnvironmentValue(environment, key, value) {
  const variable = environment.values.find(item => item.key === key);
  if (variable) variable.value = value;
  else environment.values.push({ key, value, type: 'default', enabled: true });
}

function canConnect(baseUrl) {
  const address = new URL(baseUrl);
  const port = Number(address.port || (address.protocol === 'https:' ? 443 : 80));
  return new Promise(resolve => {
    const socket = net.createConnection({ host: address.hostname, port });
    const finish = reachable => {
      socket.destroy();
      resolve(reachable);
    };
    socket.setTimeout(2500, () => finish(false));
    socket.once('connect', () => finish(true));
    socket.once('error', () => finish(false));
  });
}

function redactJsonReport(file, secrets) {
  let report;
  try {
    report = JSON.parse(readFileSync(file, 'utf8'));
  } catch {
    return;
  }

  const secretValues = new Set(secrets.filter(value => typeof value === 'string' && value.length > 0));
  const collectSecrets = (value, propertyName = '') => {
    if (/password|access.?token|refresh.?token|authorization/i.test(propertyName) &&
        typeof value === 'string' && value.length > 0) secretValues.add(value);
    if (Array.isArray(value)) value.forEach(item => collectSecrets(item));
    else if (value && typeof value === 'object')
      Object.entries(value).forEach(([key, item]) => collectSecrets(item, key));
  };
  collectSecrets(report);
  const redact = (value, propertyName = '') => {
    if (/password|access.?token|refresh.?token|authorization/i.test(propertyName)) return '[REDACTED]';
    if (Array.isArray(value)) return value.map(item => redact(item));
    if (value && typeof value === 'object') {
      return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, redact(item, key)]));
    }
    if (typeof value === 'string') {
      return [...secretValues].reduce((result, secret) => result.split(secret).join('[REDACTED]'), value);
    }
    return value;
  };

  writeFileSync(file, `${JSON.stringify(redact(report), null, 2)}\n`);
}

const local = parseLocalEnvironment();
const adminEmail = process.env.CLASSROOM_ADMIN_EMAIL || process.env.BOOTSTRAP_ADMIN_EMAIL || local.BOOTSTRAP_ADMIN_EMAIL || '';
const adminPassword = process.env.CLASSROOM_ADMIN_PASSWORD || process.env.BOOTSTRAP_ADMIN_PASSWORD || local.BOOTSTRAP_ADMIN_PASSWORD || '';
if (!adminEmail || !adminPassword) {
  console.error('Set CLASSROOM_ADMIN_EMAIL and CLASSROOM_ADMIN_PASSWORD, or configure the local bootstrap Admin values in .env.');
  process.exit(2);
}

const environment = JSON.parse(readFileSync(environmentPath, 'utf8'));
const identityPort = process.env.IDENTITY_API_PORT || local.IDENTITY_API_PORT || '6001';
const classroomPort = process.env.CLASSROOM_API_PORT || local.CLASSROOM_API_PORT || '6003';
setEnvironmentValue(environment, 'identityBaseUrl', process.env.IDENTITY_API_BASE_URL || `http://localhost:${identityPort}`);
setEnvironmentValue(environment, 'classroomBaseUrl', process.env.CLASSROOM_API_BASE_URL || `http://localhost:${classroomPort}`);
setEnvironmentValue(environment, 'adminEmail', adminEmail);
setEnvironmentValue(environment, 'adminPassword', adminPassword);

const identityBaseUrl = environment.values.find(item => item.key === 'identityBaseUrl').value;
const classroomBaseUrl = environment.values.find(item => item.key === 'classroomBaseUrl').value;
const available = await Promise.all([canConnect(identityBaseUrl), canConnect(classroomBaseUrl)]);
if (available.some(isAvailable => !isAvailable)) {
  const blocked = [identityBaseUrl, classroomBaseUrl].filter((_, index) => !available[index]);
  console.error(`Newman tests not started. API prerequisite unavailable: ${blocked.join(', ')}. Start Identity and Classroom, then rerun.`);
  process.exit(2);
}

mkdirSync(reportDirectory, { recursive: true });
const privateDirectory = mkdtempSync(path.join(os.tmpdir(), 'toeic-classroom-newman-'));
const privateEnvironment = path.join(privateDirectory, 'environment.json');
writeFileSync(privateEnvironment, JSON.stringify(environment), { mode: 0o600 });

const args = [
  'run', collectionPath,
  '--environment', privateEnvironment,
  '--reporters', 'cli,json,junit',
  '--reporter-json-export', jsonReport,
  '--reporter-junit-export', junitReport
];

let exitCode = 1;
try {
  const result = spawnSync('newman', args, { cwd: root, stdio: 'inherit' });
  if (result.error) {
    console.error(`Could not start Newman: ${result.error.message}. Run npm install first.`);
  } else {
    exitCode = result.status ?? 1;
  }
} finally {
  redactJsonReport(jsonReport, [adminPassword]);
  rmSync(privateDirectory, { recursive: true, force: true });
  console.log(`JSON report: ${path.relative(root, jsonReport)}`);
  console.log(`JUnit report: ${path.relative(root, junitReport)}`);
}

process.exitCode = exitCode;
