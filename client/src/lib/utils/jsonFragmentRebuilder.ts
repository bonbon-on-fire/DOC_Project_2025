import type { JsonFragmentKind, JsonFragmentUpdate } from '../chat/sseEventTypes';

// Rebuilds a structured value incrementally from JsonFragmentUpdates
export class JsonFragmentRebuilder {
  private root: any = undefined;
  private partialBuffers = new Map<string, string>();
  private complete = false;

  apply(updates: JsonFragmentUpdate[]): void {
    for (const u of updates) {
      switch (u.kind as JsonFragmentKind) {
        case 'StartObject': {
          this.ensureContainer(u.path, 'object');
          break;
        }
        case 'EndObject': {
          // no-op; structure already ensured on StartObject/sets
          break;
        }
        case 'StartArray': {
          this.ensureContainer(u.path, 'array');
          break;
        }
        case 'EndArray': {
          // no-op
          break;
        }
        case 'Key': {
          // Keys are path hints; no mutation needed
          break;
        }
        case 'StartString': {
          // ignore, PartialString/CompleteString carry content
          break;
        }
        case 'PartialString': {
          const path = u.path;
          const chunk = u.textValue ?? '';
          const prev = this.partialBuffers.get(path) ?? '';
          const next = prev + chunk;
          this.partialBuffers.set(path, next);
          this.setAtPath(path, next);
          break;
        }
        case 'CompleteString': {
          const path = u.path;
          // Prefer textValue if provided; includes quotes (e.g., "Hello"). Parse to string.
          let val: string = '';
          if (typeof u.textValue === 'string' && u.textValue.length > 0) {
            try {
              val = JSON.parse(u.textValue);
            } catch {
              // Fallback: if textValue lacked quotes, use as-is.
              val = u.textValue;
            }
          } else {
            // If no textValue, try buffered partial or value
            const buf = this.partialBuffers.get(path);
            if (buf != null) val = buf;
            else if (typeof u.value === 'string') val = u.value;
          }
          this.partialBuffers.delete(path);
          this.setAtPath(path, val);
          break;
        }
        case 'CompleteNumber': {
          const path = u.path;
          const raw = u.textValue ?? (u.value as any);
          const num = typeof raw === 'number' ? raw : Number(raw);
          this.setAtPath(path, num);
          break;
        }
        case 'CompleteBoolean': {
          const path = u.path;
          const raw = u.textValue ?? (u.value as any);
          const bool = typeof raw === 'boolean' ? raw : String(raw).toLowerCase() === 'true';
          this.setAtPath(path, bool);
          break;
        }
        case 'CompleteNull': {
          this.setAtPath(u.path, null);
          break;
        }
        case 'JsonComplete': {
          this.complete = true;
          break;
        }
        default: {
          // ignore unknown kinds
          break;
        }
      }
    }
  }

  getValue(): any {
    return this.root;
  }

  isComplete(): boolean {
    return this.complete;
  }

  private ensureContainer(path: string, kind: 'object' | 'array'): void {
    const segs = this.parsePath(path);
    if (segs.length === 0) {
      // root container
      if (this.root === undefined) this.root = kind === 'object' ? {} : [];
      return;
    }
    const parentPath = segs.slice(0, -1);
    const last = segs[segs.length - 1];
    const parent = this.getOrCreate(parentPath);
    if (typeof last === 'string') {
      if (parent[last] === undefined) parent[last] = kind === 'object' ? {} : [];
    } else {
      // array index; ensure array exists and init element if necessary
      if (!Array.isArray(parent)) return; // defensive; path malformed
      if (parent[last] === undefined) parent[last] = kind === 'object' ? {} : [];
    }
  }

  private setAtPath(path: string, value: any): void {
    const segs = this.parsePath(path);
    if (segs.length === 0) {
      this.root = value;
      return;
    }
    const parent = this.getOrCreate(segs.slice(0, -1));
    const last = segs[segs.length - 1];
    if (typeof last === 'string') {
      parent[last] = value;
    } else {
      // array index
      if (!Array.isArray(parent)) {
        // coerce into array if wrong type
        return;
      }
      parent[last] = value;
    }
  }

  private getOrCreate(pathSegs: (string | number)[]): any {
    if (pathSegs.length === 0) {
      if (this.root === undefined) this.root = {};
      return this.root;
    }
    if (this.root === undefined) this.root = {};
    let cur = this.root;
    for (let i = 0; i < pathSegs.length; i++) {
      const seg = pathSegs[i];
      const nextSeg = i + 1 < pathSegs.length ? pathSegs[i + 1] : undefined;
      if (typeof seg === 'string') {
        if (cur[seg] === undefined) cur[seg] = typeof nextSeg === 'number' ? [] : {};
        cur = cur[seg];
      } else {
        // array index
        if (!Array.isArray(cur)) {
          // if not array, coerce
          return cur;
        }
        if (cur[seg] === undefined) cur[seg] = typeof nextSeg === 'number' ? [] : {};
        cur = cur[seg];
      }
    }
    return cur;
  }

  private parsePath(path: string): (string | number)[] {
    // Expect format like: root, root.foo, root.items[0].name
    let p = path.trim();
    if (p.startsWith('root')) p = p.slice(4);
    if (p.startsWith('.')) p = p.slice(1);
    if (!p) return [];

    const segs: (string | number)[] = [];
    // Split by '.' but keep array indices
    const parts = p.split('.');
    for (const part of parts) {
      if (!part) continue;
      // Extract name and any [index] chains
      const re = /([^\[]+)(\[[0-9]+\])*/g;
      let m: RegExpExecArray | null;
      while ((m = re.exec(part)) !== null) {
        const name = m[1];
        if (name) segs.push(name);
        const rest = part.slice(m.index + name.length);
        const indexRe = /\[([0-9]+)\]/g;
        let im: RegExpExecArray | null;
        while ((im = indexRe.exec(rest)) !== null) {
          segs.push(Number(im[1]));
        }
        break; // only first match needed due to global slice
      }
    }
    return segs;
  }
}

export default JsonFragmentRebuilder;

