import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";

// An outer npx can pass down its user-agent, so read the CLI actually running this script.
let npmMajor = 0;
try {
  const execPath = process.env.npm_execpath;
  if (execPath) {
    const packageJson = JSON.parse(
      readFileSync(join(dirname(execPath), "..", "package.json"), "utf8"),
    );
    if (packageJson.name === "npm") npmMajor = Number(packageJson.version.split(".")[0]);
  }
} catch {
  // Unknown package managers and layouts fail closed.
}

if (!Number.isInteger(npmMajor) || npmMajor < 12) {
  console.error("HeroParser's JavaScript workspace requires npm 12 or newer.");
  process.exitCode = 1;
}
