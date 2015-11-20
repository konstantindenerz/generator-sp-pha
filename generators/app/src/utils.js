module.exports = {
  validators: {
    _issuerId: function(value){
      return /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/.test(value)
    }
  }
}
