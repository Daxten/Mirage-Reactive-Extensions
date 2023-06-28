# MirageReactiveExtensions

Provides Reactive extensions to Mirage. 

## Requirements
* Mirage >v145

## Download

use package manager to get versions easily, or replace `#v0.0.4` with the tag, branch or full hash of the commit.

IMPORTANT: update `v0.0.4` with latest version on release page
```json
"de.daxten.mirage-reactive-extensions": "https://github.com/Daxten/Mirage-Reactive-Extensions.git?path=/Assets/MirageReactiveExtensions#v0.0.4",
```

## SyncVar
`SyncVar<T> MyVar` is similar to `[SyncVar] T MyVar` but lifts the Value into `AsyncReactiveProperty<T>`.

```c#
class A extends NetworkBehaviour {
   public SyncVar<int> Health = new(100);
   
   [Server]
   public void OnHit() { Health.Value -= 10; }
}

class B extends NetworkBehaviour {
   public void ObserveA(A a) {
      Health.Where(n => n <= 0).ForEachAsync(e => Debug.Log("My Friend A just died!")), destroyCancellationToken);
   }
}
```

## SyncLink
`SyncLink<T>` allows you to create a Link/Relation to another Network Object. SyncLink manages all problems associated with this.

* If the object is not spawned yet, it will await for it to spawn and then start syncing it
* If the object gets destroyed, SyncLink will automaticly become "null". All events are triggered as expected.
* SyncLink cleans after itself, no `.Dispose()` or anything required. 

You can link objects ahead of time inside the editor.

```c#
class A extends NetworkBehaviour { }

class B extends NetworkBehaviour {
   SyncLink<A> Linked = new();
}
```

## SyncLinks
`SyncLinks<T>` allows you to create a Link/Relation to a Set of other Network Objects. SyncLinks manages all problems associated with this.

* If an object is not spawned yet, it will await for it to spawn and then add and sync it.
* If any object gets destroyed, SyncLinks will automaticly remove it from the set. All events are triggered as expected.
* SyncLinks cleans after itself, no `.Dispose()` or anything required.

You can't use the edit at the moment to set `SyncLinks<T>` ahead of time.

```c#
class A extends NetworkBehaviour { }

class B extends NetworkBehaviour {
   SyncLinks<A> Linked = new();
}
```

## Editor
To get full Editor support for these, make sure to add the following Compiler Flag to remove the default Mirage Editor:
* Edit
* Project Settings
* Player
* Other Settings
* Script Compilation
* Add `EXCLUDE_NETWORK_BEHAVIOUR_INSPECTOR`

## Bugs?

Please report any bugs or issues [Here](https://github.com/Daxten/Mirage-Reactive-Extensions/issues)

# Goals

- Simplify Complex Sync Problems
