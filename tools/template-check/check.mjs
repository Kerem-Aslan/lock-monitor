// Expands the composed widget template and the settings card with sample data, for every size
// and for both hosts (Widgets board / lock screen), using the official templating engine.
// Fails when an unbound "${...}" survives or when a template renders with the wrong data.
//
//   dotnet run --project tools/LockMonitor.Probe -c Release -- --seconds 3 --write-cards artifacts/cards
//   cd tools/template-check && npm ci && node check.mjs ../../artifacts/cards
import { readFileSync } from "node:fs";
import { join } from "node:path";
import * as ACData from "adaptivecards-templating";

const dir = process.argv[2] ?? "../../artifacts/cards";
const read = (name) => JSON.parse(readFileSync(join(dir, name), "utf8"));

const cardTemplate = read("template-card.json");
const settingsTemplate = read("template-settings.json");
const settingsData = read("data-settings.json");

let failures = 0;
function check(name, condition, detail = "") {
  console.log(`${condition ? "ok  " : "FAIL"} ${name}${detail ? "  " + detail : ""}`);
  if (!condition) failures++;
}

function expand(template, data, host) {
  return new ACData.Template(template).expand({ $root: data, $host: host });
}

const unbound = (card) => JSON.stringify(card).match(/\$\{[^}]*\}/g) ?? [];

for (const size of ["small", "medium", "large"]) {
  for (const signedIn of [true, false]) {
    const host = { widgetSize: size, hostTheme: "dark", isUserContextAuthenticated: signedIn };
    const where = `${size}/${signedIn ? "board" : "lock screen"}`;

    for (const dataFile of [`data-${size}.json`, "data-pending.json"]) {
      const card = expand(cardTemplate, read(dataFile), host);
      const body = card.body ?? [];
      check(`${where} with ${dataFile}: one body container`, body.length === 1, `got ${body.length}`);
      check(`${where} with ${dataFile}: everything bound`, unbound(card).length === 0, unbound(card).join(" "));
      check(`${where}: tap action only on the board`, signedIn === Boolean(body[0]?.selectAction));
    }

    // The host may pair the card template with the settings data for a moment: draw nothing.
    const mismatched = expand(cardTemplate, settingsData, host);
    check(`${where}: card template + settings data renders nothing`, (mismatched.body ?? []).length === 0);
  }
}

const settings = expand(settingsTemplate, settingsData, { widgetSize: "large" });
check("settings card: everything bound", unbound(settings).length === 0, unbound(settings).join(" "));
const settingsMismatch = expand(settingsTemplate, read("data-medium.json"), { widgetSize: "large" });
check("settings template + card data renders no inputs", (settingsMismatch.body ?? []).length === 0);

console.log(failures === 0 ? "\nAll template checks passed." : `\n${failures} template check(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
