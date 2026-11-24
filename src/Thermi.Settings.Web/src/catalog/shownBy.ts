import type { ItemView, Settings } from '../bridge/bridge.g';
import { itemName, overlayName } from '../list/rows';
import type { TextKey } from '../texts/Texts';

type Say = (key: TextKey, ...blanks: (string | number)[]) => string;

/**
 * The names of the items that show a sensor already, by the sensor itself or as their auto's pick. With more than one
 * overlay each names its overlay too, or two items alike on two overlays would read as one name said twice.
 */
export function shownBy(settings: Settings, views: Readonly<Record<string, ItemView>>, sensor: string, say: Say): string[] {
  const several = settings.overlays.length > 1;
  return settings.overlays.flatMap((overlay, index) =>
    overlay.items
      .filter((item) => item.sensor === sensor || views[item.id]?.auto === sensor)
      .map((item) => (several ? say('CatalogText.ShownOn', itemName(item), overlayName(overlay, index, say)) : itemName(item))),
  );
}
