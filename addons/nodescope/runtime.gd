extends Node
## NodeScope runtime bridge. Listens only on loopback; newline-delimited JSON messages.
const PORT := 47831
var server := TCPServer.new()
var peer: StreamPeerTCP
var input_buffer := ""
var free_camera: Camera3D
var previous_camera: Camera3D
var camera_speed := 8.0
var camera_enabled := false
var selected_id := 0
var runtime_logs: Array[String] = []

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    var err := server.listen(PORT, "127.0.0.1")
    if err != OK:
        push_error("NodeScope could not listen on 127.0.0.1:%d (error %d)" % [PORT, err])
    print("NodeScope bridge listening on 127.0.0.1:%d" % PORT)
    _record_log("Bridge listening on 127.0.0.1:%d" % PORT)
    _ensure_editor_ui.call_deferred()

func _process(delta: float) -> void:
    if peer == null and server.is_connection_available():
        peer = server.take_connection()
        input_buffer = ""
    if peer == null:
        return
    peer.poll()
    if peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
        peer = null
        return
    var available := peer.get_available_bytes()
    if available > 0:
        input_buffer += peer.get_utf8_string(available)
        while input_buffer.contains("\n"):
            var cut := input_buffer.find("\n")
            var line := input_buffer.substr(0, cut)
            input_buffer = input_buffer.substr(cut + 1)
            _handle_line(line)
    if camera_enabled:
        _move_camera(delta)

func _handle_line(line: String) -> void:
    var parsed = JSON.parse_string(line)
    if typeof(parsed) != TYPE_DICTIONARY:
        _send({"ok": false, "error": "Invalid JSON"})
        return
    var id = parsed.get("id", 0)
    var action = str(parsed.get("action", ""))
    var args: Dictionary = parsed.get("args", {})
    var result: Variant = null
    match action:
        "ping": result = {"message": "NodeScope connected", "port": PORT}
        "tree": result = {"nodes": _tree_snapshot(get_tree().root)}
        "inspect": result = _inspect(int(args.get("id", 0)))
        "set_property": result = _set_property(int(args.get("id", 0)), str(args.get("property", "")), args.get("value"))
        "create": result = _create_node(int(args.get("parent", 0)), str(args.get("type", "box")))
        "delete": result = _delete_node(int(args.get("id", 0)))
        "duplicate": result = _duplicate_node(int(args.get("id", 0)))
        "rename": result = _rename_node(int(args.get("id", 0)), str(args.get("name", "")))
        "camera": result = _camera_command(args)
        "logs": result = {"entries": runtime_logs.duplicate(), "scope": "NodeScope bridge events only; engine output remains in Godot."}
        _: result = {"error": "Unknown action: " + action}
    _send({"id": id, "ok": not (typeof(result) == TYPE_DICTIONARY and result.has("error")), "result": result})

func _record_log(message: String) -> void:
    runtime_logs.append("[%s] %s" % [Time.get_time_string_from_system(), message])
    if runtime_logs.size() > 200: runtime_logs.pop_front()

func _send(data: Dictionary) -> void:
    if peer != null and peer.get_status() == StreamPeerTCP.STATUS_CONNECTED:
        peer.put_data((JSON.stringify(data) + "\n").to_utf8_buffer())

func _find_node(instance_id: int) -> Node:
    return instance_from_id(instance_id) as Node

func _tree_snapshot(node: Node) -> Array:
    var children: Array = []
    for child in node.get_children():
        children.append({"id": child.get_instance_id(), "name": child.name, "type": child.get_class(), "children": _tree_snapshot(child)})
    return children

func _inspect(instance_id: int) -> Dictionary:
    var node := _find_node(instance_id)
    if node == null: return {"error": "Node no longer exists"}
    var props: Array = []
    for item in node.get_property_list():
        var pname := str(item.name)
        if pname in ["name", "position", "rotation", "scale", "visible", "size", "radius", "height"]:
            var value = node.get(pname)
            props.append({"name": pname, "value": _format_value(value), "type": int(item.type)})
    return {"id": instance_id, "name": node.name, "type": node.get_class(), "properties": props}

func _format_value(value: Variant) -> String:
    if value is Vector3: return "[%s, %s, %s]" % [value.x, value.y, value.z]
    return str(value)

func _set_property(instance_id: int, property: String, raw_value: Variant) -> Dictionary:
    var node := _find_node(instance_id)
    if node == null: return {"error": "Node no longer exists"}
    var allowed := ["position", "rotation", "scale", "visible", "size", "radius", "height", "name"]
    if not allowed.has(property): return {"error": "Property is not editable"}
    if property == "name":
        node.name = str(raw_value)
    elif property == "visible" and node is CanvasItem:
        node.visible = raw_value == true or str(raw_value).to_lower() == "true"
    elif property in ["position", "rotation", "scale"] and node is Node3D:
        var v = _parse_vector(raw_value)
        if v == null: return {"error": "Expected a vector as [x,y,z]"}
        if property == "position": node.position = v
        elif property == "rotation": node.rotation_degrees = v
        else: node.scale = v
    elif property == "size" and node is CSGBox3D:
        var v = _parse_vector(raw_value)
        if v == null: return {"error": "Expected a vector as [x,y,z]"}
        node.size = v
    elif property == "radius" and node is CSGSphere3D:
        node.radius = float(raw_value)
    elif property == "height" and node is CSGCylinder3D:
        node.height = float(raw_value)
    else:
        return {"error": "Property does not apply to this node"}
    return {"updated": property}

func _parse_vector(value: Variant):
    if typeof(value) == TYPE_ARRAY and value.size() == 3:
        return Vector3(float(value[0]), float(value[1]), float(value[2]))
    if typeof(value) == TYPE_STRING:
        var cleaned := str(value).replace("Vector3", "").replace("[", "").replace("]", "").replace("(", "").replace(")", "")
        var parts = cleaned.split(",")
        if parts.size() == 3: return Vector3(float(parts[0]), float(parts[1]), float(parts[2]))
    return null

func _create_node(parent_id: int, kind: String) -> Dictionary:
    var parent := _find_node(parent_id)
    if parent == null: parent = get_tree().current_scene
    if parent == null: return {"error": "No active scene"}
    var node: Node3D
    match kind:
        "sphere": node = CSGSphere3D.new()
        "cylinder": node = CSGCylinder3D.new()
        "box": node = CSGBox3D.new()
        "mesh": 
            var mesh_node := MeshInstance3D.new()
            mesh_node.mesh = BoxMesh.new()
            node = mesh_node
        _:
            return {"error": "Unsupported primitive"}
    node.name = "NodeScope" + kind.capitalize()
    parent.add_child(node)
    node.owner = get_tree().edited_scene_root if Engine.is_editor_hint() else get_tree().current_scene
    _record_log("Created %s" % node.name)
    return {"id": node.get_instance_id(), "name": node.name, "type": node.get_class()}

func _delete_node(instance_id: int) -> Dictionary:
    var node := _find_node(instance_id)
    if node == null or node == get_tree().root or node == get_tree().current_scene:
        return {"error": "Cannot delete this node"}
    node.queue_free()
    _record_log("Deleted %s" % node.name)
    return {"deleted": instance_id}

func _duplicate_node(instance_id: int) -> Dictionary:
    var node := _find_node(instance_id)
    if node == null or node == get_tree().root: return {"error": "Node not found"}
    var copy := node.duplicate()
    node.get_parent().add_child(copy)
    copy.owner = node.owner
    copy.name = node.name + "Copy"
    _record_log("Duplicated %s" % node.name)
    return {"id": copy.get_instance_id(), "name": copy.name}

func _rename_node(instance_id: int, new_name: String) -> Dictionary:
    var node := _find_node(instance_id)
    if node == null or new_name.strip_edges().is_empty(): return {"error": "Invalid node or name"}
    node.name = new_name
    _record_log("Renamed node to %s" % node.name)
    return {"name": node.name}

func _camera_command(args: Dictionary) -> Dictionary:
    var command := str(args.get("command", "toggle"))
    if command == "speed":
        camera_speed = clampf(float(args.get("value", camera_speed)), 0.1, 1000.0)
        return {"speed": camera_speed}
    if command == "toggle":
        camera_enabled = not camera_enabled
        if camera_enabled:
            previous_camera = get_viewport().get_camera_3d()
            free_camera = Camera3D.new()
            free_camera.name = "NodeScopeFreeCamera"
            get_tree().current_scene.add_child(free_camera)
            if previous_camera: free_camera.global_transform = previous_camera.global_transform
            free_camera.current = true
        elif is_instance_valid(free_camera):
            free_camera.queue_free()
            free_camera = null
            if is_instance_valid(previous_camera): previous_camera.current = true
            previous_camera = null
        return {"enabled": camera_enabled}
    return {"error": "Unknown camera command"}

func _move_camera(delta: float) -> void:
    if not is_instance_valid(free_camera): return
    var direction := Vector3.ZERO
    if Input.is_key_pressed(KEY_W): direction -= free_camera.global_basis.z
    if Input.is_key_pressed(KEY_S): direction += free_camera.global_basis.z
    if Input.is_key_pressed(KEY_A): direction -= free_camera.global_basis.x
    if Input.is_key_pressed(KEY_D): direction += free_camera.global_basis.x
    if Input.is_key_pressed(KEY_E): direction += Vector3.UP
    if Input.is_key_pressed(KEY_Q): direction -= Vector3.UP
    free_camera.global_position += direction.normalized() * camera_speed * delta


# In-game inspector UI. The editor is session-only and runs in the game's own SceneTree.
var _ui_layer: CanvasLayer
var _panel: PanelContainer
var _tree_view: Tree
var _property_list: VBoxContainer
var _selected_runtime_node: int = 0
var _ui_visible := false

func _input(event: InputEvent) -> void:
    if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_EQUAL:
        _ui_visible = not _ui_visible
        if is_instance_valid(_panel):
            _panel.visible = _ui_visible
        get_viewport().set_input_as_handled()

func _ensure_editor_ui() -> void:
    if is_instance_valid(_ui_layer):
        return
    _ui_layer = CanvasLayer.new()
    _ui_layer.layer = 100
    get_tree().root.add_child.call_deferred(_ui_layer)
    var root := Control.new()
    root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    _ui_layer.add_child(root)
    _panel = PanelContainer.new()
    _panel.position = Vector2(24, 24)
    _panel.size = Vector2(920, 620)
    _panel.custom_minimum_size = Vector2(920, 620)
    _panel.visible = false
    root.add_child(_panel)
    var outer := VBoxContainer.new()
    _panel.add_child(outer)
    var header := HBoxContainer.new()
    outer.add_child(header)
    var heading := Label.new()
    heading.text = "NodeScope  |  Wobble Works Runtime Inspector"
    heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    header.add_child(heading)
    var close := Button.new()
    close.text = "Close  [=]"
    close.pressed.connect(func(): _ui_visible = false; _panel.visible = false)
    header.add_child(close)
    var split := HSplitContainer.new()
    split.size_flags_vertical = Control.SIZE_EXPAND_FILL
    outer.add_child(split)
    _tree_view = Tree.new()
    _tree_view.custom_minimum_size.x = 330
    _tree_view.size_flags_vertical = Control.SIZE_EXPAND_FILL
    _tree_view.item_selected.connect(_on_ui_tree_selected)
    split.add_child(_tree_view)
    var right := VBoxContainer.new()
    right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    split.add_child(right)
    var actions := HFlowContainer.new()
    right.add_child(actions)
    _add_action_button(actions, "Add Box", func(): _ui_create("box"))
    _add_action_button(actions, "Add Sphere", func(): _ui_create("sphere"))
    _add_action_button(actions, "Add Cylinder", func(): _ui_create("cylinder"))
    _add_action_button(actions, "Duplicate", func(): _ui_duplicate())
    _add_action_button(actions, "Rename", func(): _ui_rename())
    _add_action_button(actions, "Delete", func(): _ui_delete())
    var scroll := ScrollContainer.new()
    scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
    right.add_child(scroll)
    _property_list = VBoxContainer.new()
    _property_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    scroll.add_child(_property_list)
    var footer := Label.new()
    footer.text = "Runtime-only edits • Not written to the game's project files"
    outer.add_child(footer)
    _refresh_ui_tree()

func _add_action_button(parent: Control, caption: String, action: Callable) -> void:
    var button := Button.new()
    button.text = caption
    button.pressed.connect(action)
    parent.add_child(button)

func _refresh_ui_tree() -> void:
    if not is_instance_valid(_tree_view):
        return
    _tree_view.clear()
    var root_item := _tree_view.create_item()
    root_item.set_text(0, "SceneTree")
    root_item.set_metadata(0, 0)
    _append_ui_tree(root_item, get_tree().root)
    root_item.collapsed = false

func _append_ui_tree(parent_item: TreeItem, node: Node) -> void:
    for child in node.get_children():
        var item := _tree_view.create_item(parent_item)
        item.set_text(0, "%s  <%s>" % [child.name, child.get_class()])
        item.set_metadata(0, child.get_instance_id())
        _append_ui_tree(item, child)

func _on_ui_tree_selected() -> void:
    var item := _tree_view.get_selected()
    if item == null:
        return
    _selected_runtime_node = int(item.get_metadata(0))
    _populate_ui_properties()

func _populate_ui_properties() -> void:
    for child in _property_list.get_children():
        child.queue_free()
    var node := _find_node(_selected_runtime_node)
    if node == null:
        return
    var title := Label.new()
    title.text = "%s  (%s)" % [node.name, node.get_class()]
    _property_list.add_child(title)
    var editable := ["name", "position", "rotation", "scale", "visible", "size", "radius", "height"]
    for property_name in editable:
        if not _node_has_property(node, property_name):
            continue
        var row := HBoxContainer.new()
        _property_list.add_child(row)
        var label := Label.new()
        label.text = property_name
        label.custom_minimum_size.x = 110
        row.add_child(label)
        var field := LineEdit.new()
        field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        field.text = str(node.get(property_name))
        row.add_child(field)
        var apply := Button.new()
        apply.text = "Apply"
        apply.pressed.connect(func(): _apply_ui_property(property_name, field.text))
        row.add_child(apply)

func _node_has_property(node: Object, property_name: String) -> bool:
    for descriptor in node.get_property_list():
        if str(descriptor.name) == property_name:
            return true
    return false

func _apply_ui_property(property_name: String, raw: String) -> void:
    var node := _find_node(_selected_runtime_node)
    if node == null:
        return
    var result := _set_property(_selected_runtime_node, property_name, raw)
    if result.has("error"):
        _record_log("Property edit failed: " + str(result.error))
    else:
        _record_log("Updated %s.%s" % [node.name, property_name])
    _populate_ui_properties()

func _ui_create(kind: String) -> void:
    var result := _create_node(_selected_runtime_node, kind)
    if result.has("error"):
        _record_log(str(result.error))
    _refresh_ui_tree()

func _ui_duplicate() -> void:
    if _selected_runtime_node != 0:
        _duplicate_node(_selected_runtime_node)
        _refresh_ui_tree()

func _ui_delete() -> void:
    if _selected_runtime_node != 0:
        _delete_node(_selected_runtime_node)
        _selected_runtime_node = 0
        _refresh_ui_tree()
        _populate_ui_properties()

func _ui_rename() -> void:
    var node := _find_node(_selected_runtime_node)
    if node == null:
        return
    var dialog := AcceptDialog.new()
    dialog.title = "Rename node"
    var field := LineEdit.new()
    field.text = node.name
    dialog.add_child(field)
    get_tree().root.add_child(dialog)
    dialog.confirmed.connect(func():
        _rename_node(_selected_runtime_node, field.text)
        dialog.queue_free()
        _refresh_ui_tree()
    )
    dialog.canceled.connect(func(): dialog.queue_free())
    dialog.popup_centered(Vector2i(360, 120))
