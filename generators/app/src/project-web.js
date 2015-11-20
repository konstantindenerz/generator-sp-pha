'use strict';
var chalk = require('chalk'),
  yosay = require('yosay'),
  path = require('path'),
  vsname = require('vs_projectname'),
  uuid = require('uuid');



module.exports = function(Generator) {
    Generator.prototype.copyWeb = function(){
      var self = this;
      var directory = path.join('src',this.settings.solutionName + this.settings.solutionWebSuffix);
      this.sourceRoot(path.join(__dirname, '../templates/Web'));
      function cpt(source, target){
        self.fs.copyTpl(self.templatePath(source),
        self.destinationPath(path.join(directory, target ? target : source )), self.settings);
      }
      function cp(source, target){
        self.copy(path.join(self.sourceRoot(),source),
        path.join(directory, target ? target : source ));
      }
      cpt('Web.csproj', this.settings.solutionName + this.settings.solutionWebSuffix + '.csproj');
      cpt('Web.config');
      cpt('Web.Debug.config');
      cpt('Web.Release.config');
      cpt('TokenHelper.cs');
      cpt('SharePointContext.cs');
      cpt('Global.asax.cs');
      cpt('Global.asax');
      cp('packages.config');
      cp('favicon.ico');
      //cp('App_Data');
      //cp('Models');
      cpt('Properties/AssemblyInfo.cs');
      cpt('Filters/**/**.*', 'Filters');
      cpt('Controllers/**/**.*', 'Controllers');
      cpt('App_Start/**/**.*', 'App_Start');
      cpt('Views/**/**.*', 'Views');
    };
};
